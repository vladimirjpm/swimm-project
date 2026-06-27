using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

public class ResultRepository : IResultRepository
{
    // Read-only контекст (swimm_ro, SELECT-only роль) — публичный read-путь не имеет
    // привилегий записи на уровне БД.
    private readonly SwimmReadDbContext _db;
    private readonly ICacheService _cache;

    private static readonly TimeSpan StaticHintsTtl  = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DynamicHintsTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResultsTtl       = TimeSpan.FromMinutes(2);

    public ResultRepository(SwimmReadDbContext db, ICacheService cache)
    {
        _db    = db;
        _cache = cache;
    }

    public async Task<(List<ResultDto> Items, bool HasMore)> GetPagedAsync(ResultFilter filter, int page, int pageSize)
    {
        pageSize = Math.Min(pageSize, 500);
        var key = ResultsCacheKey(filter, page, pageSize);

        var cached = await _cache.GetAsync<(List<ResultDto>, bool)>(key);
        if (cached != default)
            return cached;

        var query = _db.Results.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.StyleName))
            query = query.Where(r => r.Style.Name == filter.StyleName);

        if (!string.IsNullOrWhiteSpace(filter.Distance))
            query = query.Where(r => r.Distance == filter.Distance);

        if (!string.IsNullOrWhiteSpace(filter.Gender))
            query = query.Where(r => r.Gender == filter.Gender);

        if (!string.IsNullOrWhiteSpace(filter.PoolType))
            query = query.Where(r => r.Competition.PoolType == filter.PoolType);

        if (filter.DateFrom.HasValue)
            query = query.Where(r => r.CompetitionDate >= filter.DateFrom.Value);

        if (filter.DateTo.HasValue)
            query = query.Where(r => r.CompetitionDate <= filter.DateTo.Value);

        if (filter.EventId.HasValue)
            query = query.Where(r => r.Competition.EventId == filter.EventId.Value);

        if (filter.CompetitionId.HasValue)
            query = query.Where(r => r.CompetitionId == filter.CompetitionId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Competition))
            query = query.Where(r => r.Competition.Name.StartsWith(filter.Competition));

        if (!string.IsNullOrWhiteSpace(filter.Name))
            query = query.Where(r =>
                r.Swimmer.LastName.StartsWith(filter.Name) ||
                r.Swimmer.FirstName.StartsWith(filter.Name) ||
                r.Swimmer.LastNameEn.StartsWith(filter.Name) ||
                r.Swimmer.FirstNameEn.StartsWith(filter.Name));

        if (!string.IsNullOrWhiteSpace(filter.Club))
            query = query.Where(r => r.Club.Name.StartsWith(filter.Club) || r.Club.NameEn.StartsWith(filter.Club));

        // Берём pageSize + 1 чтобы определить hasMore без COUNT
        var items = await query
            .OrderByDescending(r => r.CompetitionDate)
            .ThenBy(r => r.Position)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .Select(ResultMapping.ToDto)
            .ToListAsync();

        var hasMore = items.Count > pageSize;
        if (hasMore)
            items.RemoveAt(items.Count - 1);

        var result = (items, hasMore);
        await _cache.SetAsync(key, result, ResultsTtl);
        return result;
    }

    public async Task<ResultDto?> GetByIdAsync(long id)
    {
        return await _db.Results.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(ResultMapping.ToDto)
            .FirstOrDefaultAsync();
    }

    public async Task<string[]> GetFilterHintsAsync(string field, string? q, int limit)
    {
        limit = Math.Min(limit, 50);
        var prefix = (q ?? "").Trim();
        var key = $"hints:{field}:{prefix}";

        var cached = await _cache.GetAsync<string[]>(key);
        if (cached is not null)
            return cached;

        var ttl = field is "style" or "distance" ? StaticHintsTtl : DynamicHintsTtl;

        var hints = field switch
        {
            "style" => await _db.Styles
                .OrderBy(s => s.Name)
                .Select(s => s.Name)
                .ToArrayAsync(),

            "distance" => await _db.Results
                .Select(r => r.Distance)
                .Distinct()
                .OrderBy(d => d.Length)
                .ThenBy(d => d)
                .ToArrayAsync(),

            "club" => await _db.Clubs
                .Where(c => prefix.Length == 0 || c.Name.StartsWith(prefix) || c.NameEn.StartsWith(prefix))
                .Select(c => c.Name)
                .Where(n => n.Length > 0)
                .Distinct()
                .OrderBy(n => n)
                .Take(limit)
                .ToArrayAsync(),

            "competition" => await _db.Competitions
                .Where(c => prefix.Length == 0 || c.Name.StartsWith(prefix))
                .Select(c => c.Name)
                .Where(n => n.Length > 0)
                .Distinct()
                .OrderBy(n => n)
                .Take(limit)
                .ToArrayAsync(),

            "name" when prefix.Length > 0 => await _db.Swimmers
                .Where(s => s.LastName.StartsWith(prefix))
                .Select(s => s.LastName)
                .Union(_db.Swimmers.Where(s => s.FirstName.StartsWith(prefix)).Select(s => s.FirstName))
                .Where(n => n.Length > 0)
                .OrderBy(n => n)
                .Take(limit)
                .ToArrayAsync(),

            _ => []
        };

        if (hints.Length > 0)
            await _cache.SetAsync(key, hints, ttl);

        return hints;
    }

    private static string ResultsCacheKey(ResultFilter f, int page, int pageSize) =>
        $"results:{f.StyleName}:{f.Distance}:{f.Gender}:{f.PoolType}" +
        $":{f.DateFrom:yyyyMMdd}:{f.DateTo:yyyyMMdd}:{f.Competition}:{f.EventId}:{f.CompetitionId}:{f.Name}:{f.Club}" +
        $":{page}:{pageSize}";

    public async Task<IReadOnlyList<CompetitionSourceDto>> GetSourcesAsync()
    {
        const string key = "competition-sources:all";
        var cached = await _cache.GetAsync<IReadOnlyList<CompetitionSourceDto>>(key);
        if (cached is not null)
            return cached;

        // Многодневные события — сворачиваем в одну запись, агрегируя по дням.
        // Флаги по дням: masters/award — у ЛЮБОГО дня; show_combine — у ВСЕХ дней
        // (как !Any(!combine), чтобы EF надёжно транслировал в SQL). Пустые события пропускаем.
        var events = await _db.CompetitionEvents
            .AsNoTracking()
            .Where(e => _db.Competitions.Any(c => c.EventId == e.Id))
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.StartDate,
                e.EndDate,
                DayCount = _db.Competitions.Count(c => c.EventId == e.Id),
                PoolType = _db.Competitions.Where(c => c.EventId == e.Id).Select(c => c.PoolType).FirstOrDefault(),
                IsMasters = _db.Competitions.Any(c => c.EventId == e.Id && c.IsMasters),
                IsAward = _db.Competitions.Any(c => c.EventId == e.Id && c.IsAward),
                ShowCombine = !_db.Competitions.Any(c => c.EventId == e.Id && !c.ShowCombineAllResults),
                ResultCount = _db.Results.Count(r => r.Competition.EventId == e.Id)
            })
            .ToListAsync();

        // Однодневные соревнования (без события).
        var singles = await _db.Competitions
            .AsNoTracking()
            .Where(c => c.EventId == null)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Date,
                c.PoolType,
                c.IsMasters,
                c.IsAward,
                c.ShowCombineAllResults,
                ResultCount = _db.Results.Count(r => r.CompetitionId == c.Id)
            })
            .ToListAsync();

        static string Fmt(DateOnly? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";

        var items = new List<(DateOnly Sort, CompetitionSourceDto Dto)>(events.Count + singles.Count);

        foreach (var e in events)
        {
            items.Add((e.StartDate ?? DateOnly.MinValue, new CompetitionSourceDto
            {
                Kind = "event",
                Id = e.Id,
                Name = e.Name,
                Date = Fmt(e.StartDate),
                DateEnd = e.EndDate != e.StartDate ? Fmt(e.EndDate) : null,
                PoolType = e.PoolType ?? "",
                IsMasters = e.IsMasters,
                IsAward = e.IsAward,
                ShowCombineAllResults = e.ShowCombine,
                DayCount = e.DayCount,
                ResultCount = e.ResultCount
            }));
        }

        foreach (var c in singles)
        {
            DateOnly.TryParseExact(c.Date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d);
            items.Add((d, new CompetitionSourceDto
            {
                Kind = "competition",
                Id = c.Id,
                Name = c.Name,
                Date = c.Date,
                PoolType = c.PoolType,
                IsMasters = c.IsMasters,
                IsAward = c.IsAward,
                ShowCombineAllResults = c.ShowCombineAllResults,
                DayCount = 1,
                ResultCount = c.ResultCount
            }));
        }

        var ordered = items
            .OrderByDescending(x => x.Sort)
            .ThenBy(x => x.Dto.Name)
            .Select(x => x.Dto)
            .ToList();

        await _cache.SetAsync(key, (IReadOnlyList<CompetitionSourceDto>)ordered, TimeSpan.FromMinutes(5));
        return ordered;
    }
}
