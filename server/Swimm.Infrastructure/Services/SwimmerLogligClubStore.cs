using Microsoft.EntityFrameworkCore;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Общее место последнего ответа loglig про клуб пловца (<c>Sys_SwimmerLogligClubs</c>, Р112):
/// читают и пишут сверка кандидатов на смену клуба (Р98) и проверка пары клубов на /Admin/Clubs.
/// Пишется только удачный ответ; сохранение — у вызывающего (SaveChanges).
/// </summary>
internal static class SwimmerLogligClubStore
{
    public static async Task<Dictionary<int, StoredLogligClub>> LoadAsync(
        SwimmDbContext db, IReadOnlyCollection<int> swimmerIds, CancellationToken ct)
    {
        if (swimmerIds.Count == 0) return [];
        var ids = swimmerIds.ToArray();
        return await db.SwimmerLogligClubs.AsNoTracking()
            .Where(x => ids.Contains(x.SwimmerId))
            .ToDictionaryAsync(x => x.SwimmerId, x => new StoredLogligClub(x.LogligId, x.ClubName, x.CheckedAt), ct);
    }

    public static async Task UpsertAsync(
        SwimmDbContext db, int swimmerId, int logligId, string? clubName, DateTime checkedAt, CancellationToken ct)
    {
        var row = await db.SwimmerLogligClubs.FindAsync([swimmerId], ct);
        if (row is null)
        {
            row = new SwimmerLogligClub { SwimmerId = swimmerId };
            db.SwimmerLogligClubs.Add(row);
        }
        row.LogligId = logligId;
        row.ClubName = clubName is { Length: > 200 } ? clubName[..200] : clubName;
        row.CheckedAt = checkedAt;
    }
}
