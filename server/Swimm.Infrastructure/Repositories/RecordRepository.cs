using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
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

        await _cache.SetAsync(cacheKey, (IReadOnlyList<RecordDto>)records, CacheTtl);

        return records;
    }

    public async Task<IReadOnlyList<NormativeStandardDto>> GetStandardsAsync(string? kind = null)
    {
        var cacheKey = $"normative-standards:{kind ?? "all"}";

        var cached = await _cache.GetAsync<IReadOnlyList<NormativeStandardDto>>(cacheKey);
        if (cached is not null)
            return cached;

        var query = _db.NormativeStandards.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(kind))
            query = query.Where(s => s.Kind == kind);

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
