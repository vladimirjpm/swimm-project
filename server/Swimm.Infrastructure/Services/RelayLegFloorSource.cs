using Microsoft.EntityFrameworkCore;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Справочник для <see cref="RelayLegFloor"/>: рекорды Израиля вольным на 50/100/200 —
/// по возрастам (<c>age</c>, ступени 10–18) и взрослые (<c>open</c>; ступень «adults» туда же).
/// Один на доклейку промежуточных и на чистку уже записанных этапов — правило одно.
/// </summary>
internal static class RelayLegFloorSource
{
    private static readonly string[] Distances = ["50m", "100m", "200m"];

    public static async Task<RelayLegFloor> LoadAsync(SwimmDbContext db, CancellationToken ct)
    {
        var rows = await db.Records.AsNoTracking()
            .Where(r => r.RegionType == "country" && r.RegionCode == "ISR"
                && (r.Category == "age" || r.Category == "open")
                && r.Style == "freestyle" && Distances.Contains(r.Distance) && r.TimeMs != null)
            .Select(r => new { r.Category, r.AgeKey, r.Gender, r.PoolType, r.Distance, r.TimeMs })
            .ToListAsync(ct);

        return new RelayLegFloor(rows.Select(r => new FreestyleRecordRow(
            r.Category == "age" && int.TryParse(r.AgeKey, out var age) ? age : null,
            r.Gender, r.PoolType, int.Parse(r.Distance.TrimEnd('m')), r.TimeMs!.Value)));
    }
}
