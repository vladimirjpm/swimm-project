using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

public class ClubPointsRepository : IClubPointsRepository
{
    // Read-only контекст (swimm_ro) — правила очков публичны и статичны.
    private readonly SwimmReadDbContext _db;
    private readonly ICacheService _cache;

    private const string CacheKey = "club-points:rules";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public ClubPointsRepository(SwimmReadDbContext db, ICacheService cache)
    {
        _db    = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<ClubPointsRuleDto>> GetRulesAsync()
    {
        var cached = await _cache.GetAsync<IReadOnlyList<ClubPointsRuleDto>>(CacheKey);
        if (cached is not null)
            return cached;

        var rules = await _db.ClubPointsRules
            .AsNoTracking()
            .Include(r => r.Entries)
            .OrderBy(r => r.Id)
            .ToListAsync();

        var dtos = rules.Select(r => new ClubPointsRuleDto
        {
            Version       = r.Version,
            EffectiveFrom = r.EffectiveFrom.ToString("yyyy-MM-dd"),
            Description   = r.Description,
            Scope         = r.Scope,
            DefaultPoints = r.DefaultPoints,
            MaxScoringPlace = r.MaxScoringPlace,
            PointsByPlace = r.Entries
                .OrderBy(e => e.Place)
                .ToDictionary(e => e.Place.ToString(), e => e.Points)
        }).ToList();

        await _cache.SetAsync(CacheKey, (IReadOnlyList<ClubPointsRuleDto>)dtos, CacheTtl);

        return dtos;
    }
}
