using Microsoft.EntityFrameworkCore;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// «Мои» пловцы зрителя в группе — один круг для «Your lane» плана дорожек (L4) и строки «You»
/// ленты тренировок (docs/plans/trainings-tab-redesign-plan.md, Р4). Только подсветка, прав не
/// даёт, поэтому годятся и недоверенные источники (правило «primary favorite недоверенный» — про
/// права, не про подсветку). «me»: привязка аккаунта админом сайта (<c>AppUser.SwimmerId</c>) и
/// избранное «Me»; «family»: избранное-семья и метка членства в этой группе. Порядок — как
/// перечислено, без повторов: первым идёт тот, чья строка главнее.
/// </summary>
internal static class MySwimmersQuery
{
    public const string Me = "me";
    public const string Family = "family";

    public static async Task<List<(int SwimmerId, string Kind)>> LoadAsync(SwimmDbContext db, int hubGroupId, int userId)
    {
        var linked = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.SwimmerId)
            .FirstOrDefaultAsync();

        var favorites = await db.UserFavorites.AsNoTracking()
            .Where(f => f.UserId == userId && f.SwimmerId != null && (f.IsPrimary || f.IsFamily))
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
            .Select(f => new { SwimmerId = f.SwimmerId!.Value, f.IsPrimary })
            .ToListAsync();

        var label = await db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && m.UserId == userId && m.SwimmerId != null)
            .Select(m => m.SwimmerId)
            .FirstOrDefaultAsync();

        var ordered = new List<(int SwimmerId, string Kind)>();
        if (linked is int self) ordered.Add((self, Me));
        ordered.AddRange(favorites.Where(f => f.IsPrimary).Select(f => (f.SwimmerId, Me)));
        ordered.AddRange(favorites.Where(f => !f.IsPrimary).Select(f => (f.SwimmerId, Family)));
        if (label is int labelled) ordered.Add((labelled, Family));

        var seen = new HashSet<int>();
        return ordered.Where(x => seen.Add(x.SwimmerId)).ToList();
    }
}
