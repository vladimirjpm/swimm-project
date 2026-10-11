using Microsoft.EntityFrameworkCore;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Кем аккаунт-участник стоит в группе — пловцом состава или самим собой (Ш3.2,
/// docs/plans/entity-hero-roles-plan.md §5). Одно место для вида по дорожкам и флага «On break»:
/// иначе «кто на дорожке» и «кто на перерыве» разошлись бы.
///
/// Порядок (решение Влада 28.09.2026): метка тренера (<c>HubGroupUserMember.SwimmerId</c>, она
/// перебивает заявления) → привязка админом сайта (<c>AppUser.SwimmerId</c>) → избранное «Me» →
/// первое избранное «Family» → никто (стоит аккаунтом). Берётся только пловец ВИДИМОГО состава
/// этой группы. «Me»/«Family» — заявления, не доверие: они действуют лишь внутри группы
/// (data-integrity И15, Р55), поэтому пригодны здесь и не пригодны для витрины.
/// </summary>
internal static class HubGroupPersonResolver
{
    /// <returns>userId → пловец состава или null (стоит аккаунтом). Есть ключ у каждого из <paramref name="userIds"/>.</returns>
    public static async Task<Dictionary<int, int?>> ResolveAsync(
        SwimmDbContext db, int hubGroupId, IReadOnlyCollection<int> userIds)
    {
        var result = userIds.ToDictionary(id => id, _ => (int?)null);
        if (userIds.Count == 0) return result;

        var roster = (await db.HubGroupMembers.AsNoTracking()
                .Where(m => m.HubGroupId == hubGroupId && !m.IsExcluded)
                .Select(m => m.SwimmerId)
                .ToListAsync())
            .ToHashSet();
        if (roster.Count == 0) return result;

        var labels = await db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && m.SwimmerId != null && userIds.Contains(m.UserId))
            .ToDictionaryAsync(m => m.UserId, m => m.SwimmerId!.Value);

        var linked = await db.AppUsers.AsNoTracking()
            .Where(u => u.SwimmerId != null && userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.SwimmerId!.Value);

        var favorites = (await db.UserFavorites.AsNoTracking()
                .Where(f => f.SwimmerId != null && (f.IsPrimary || f.IsFamily) && userIds.Contains(f.UserId))
                .OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
                .Select(f => new { f.UserId, SwimmerId = f.SwimmerId!.Value, f.IsPrimary })
                .ToListAsync())
            .ToLookup(f => f.UserId);

        foreach (var userId in userIds)
        {
            var candidates = new List<int>();
            if (labels.TryGetValue(userId, out var label)) candidates.Add(label);
            if (linked.TryGetValue(userId, out var self)) candidates.Add(self);
            candidates.AddRange(favorites[userId].Where(f => f.IsPrimary).Select(f => f.SwimmerId));
            candidates.AddRange(favorites[userId].Where(f => !f.IsPrimary).Select(f => f.SwimmerId));

            foreach (var swimmerId in candidates)
            {
                if (!roster.Contains(swimmerId)) continue;
                result[userId] = swimmerId;
                break;
            }
        }
        return result;
    }
}
