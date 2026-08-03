using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

public class RecordRepository : IRecordRepository
{
    // Read-only контекст (swimm_ro) — рекорды/нормативы публичны.
    private readonly SwimmReadDbContext _db;
    private readonly ICacheService _cache;

    // Данные меняются редко (правки в админке, будущее автообновление) — длинный TTL;
    // админ-CRUD инвалидирует всё через ICacheService.InvalidateAllAsync().
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    public RecordRepository(SwimmReadDbContext db, ICacheService cache)
    {
        _db    = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<RecordDto>> GetRecordsAsync(string region, string? category = null)
    {
        // Регион нормализуем к ключу кэша: records:{region}:{category|all}
        var regionKey = region.Trim().ToUpperInvariant();
        var cacheKey = $"records:{regionKey}:{category ?? "all"}";

        var cached = await _cache.GetAsync<IReadOnlyList<RecordDto>>(cacheKey);
        if (cached is not null)
            return cached;

        var query = _db.Records.AsNoTracking();

        // "world" — тип региона; всё остальное — код континента или страны.
        query = regionKey == "WORLD"
            ? query.Where(r => r.RegionType == "world")
            : query.Where(r => r.RegionCode == regionKey);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(r => r.Category == category);

        var records = await query
            .OrderBy(r => r.Category).ThenBy(r => r.Gender).ThenBy(r => r.PoolType)
            .ThenBy(r => r.Style).ThenBy(r => r.Distance).ThenBy(r => r.AgeKey)
            .Select(r => new RecordDto
            {
                Id            = r.Id,
                RegionType    = r.RegionType,
                RegionCode    = r.RegionCode,
                Category      = r.Category,
                AgeKey        = r.AgeKey,
                Gender        = r.Gender,
                PoolType      = r.PoolType,
                Style         = r.Style,
                Distance      = r.Distance,
                Time          = r.Time,
                HolderName    = r.HolderName,
                Club          = r.Club,
                HolderCountry = r.HolderCountry,
                RecordDate    = r.RecordDate,
                UpdatedAt     = r.UpdatedAt
            })
            .ToListAsync();

        // Метка «запись оспаривается» (docs/plans/records-quality-plan.md). Отдельным запросом,
        // а не JOIN: претензий десятки на 1.9к рекордов, а ключ сопоставления — 8 осей ПЛЮС
        // время, что в SQL-джойне читалось бы куда хуже, чем словарь в памяти.
        var issues = await OpenIssuesAsync();
        foreach (var r in records)
            r.IssueReason = issues.GetValueOrDefault(RecordIssueKey.Of(
                r.RegionType, r.RegionCode, r.Category, r.AgeKey,
                r.Gender, r.PoolType, r.Style, r.Distance, r.Time));

        await _cache.SetAsync(cacheKey, (IReadOnlyList<RecordDto>)records, CacheTtl);

        return records;
    }

    /// <summary>
    /// Открытые претензии по ключу «оси + время». Закрытые (<c>rejected</c> — разобрались,
    /// запись верна; <c>fixed-by-source</c> — федерация уже исправила) метку не дают: иначе
    /// значок висел бы вечно и обесценился.
    /// </summary>
    private async Task<Dictionary<string, string>> OpenIssuesAsync()
    {
        var open = await _db.RecordIssues.AsNoTracking()
            .Where(i => i.Status == RecordIssueStatuses.Open
                     || i.Status == RecordIssueStatuses.Reported
                     || i.Status == RecordIssueStatuses.Accepted)
            .Select(i => new
            {
                i.RegionType, i.RegionCode, i.Category, i.AgeKey, i.Gender,
                i.PoolType, i.Style, i.Distance, i.FlaggedTime, i.Reason
            })
            .ToListAsync();

        var map = new Dictionary<string, string>();
        foreach (var i in open)
        {
            var key = RecordIssueKey.Of(i.RegionType, i.RegionCode, i.Category, i.AgeKey,
                i.Gender, i.PoolType, i.Style, i.Distance, i.FlaggedTime);
            map[key] = i.Reason;
        }
        return map;
    }

    public async Task<IReadOnlyList<NormativeStandardDto>> GetStandardsAsync(string? kind = null, string? country = null)
    {
        // Страну нормализуем как регион выше: trim + upper.
        var countryKey = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant();
        var cacheKey = $"normative-standards:{kind ?? "all"}:{countryKey ?? "all"}";

        var cached = await _cache.GetAsync<IReadOnlyList<NormativeStandardDto>>(cacheKey);
        if (cached is not null)
            return cached;

        var query = _db.NormativeStandards.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(kind))
            query = query.Where(s => s.Kind == kind);

        // Страна задана — отдаём её строки плюс универсальные (Country == "").
        if (countryKey is not null)
            query = query.Where(s => s.Country == countryKey || s.Country == "");

        var standards = await query
            .OrderBy(s => s.Kind).ThenBy(s => s.Gender).ThenBy(s => s.PoolType)
            .ThenBy(s => s.Style).ThenBy(s => s.Distance).ThenBy(s => s.AgeKey).ThenBy(s => s.Level)
            .Select(s => new NormativeStandardDto
            {
                Id       = s.Id,
                Kind     = s.Kind,
                Country  = s.Country,
                Gender   = s.Gender,
                PoolType = s.PoolType,
                Style    = s.Style,
                Distance = s.Distance,
                AgeKey   = s.AgeKey,
                Level    = s.Level,
                Time     = s.Time
            })
            .ToListAsync();

        await _cache.SetAsync(cacheKey, (IReadOnlyList<NormativeStandardDto>)standards, CacheTtl);

        return standards;
    }
}
