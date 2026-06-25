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
        $":{f.DateFrom:yyyyMMdd}:{f.DateTo:yyyyMMdd}:{f.Competition}:{f.Name}:{f.Club}" +
        $":{page}:{pageSize}";
}
