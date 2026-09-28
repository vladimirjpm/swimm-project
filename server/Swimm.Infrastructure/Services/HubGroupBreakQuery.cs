using Microsoft.EntityFrameworkCore;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Кто в группе на перерыве в данный день (Ш3.1) — одно место для RSVP, плана дорожек и
/// карточек: иначе «знаменатель полосы» и «кто не лезет в Unassigned» разъехались бы.
///
/// Флаг один на человека (решение Влада 28.09.2026), а строк у человека может быть две —
/// у аккаунта и у его пловца. Связка — метка членства от тренера (<c>HubGroupUserMember.SwimmerId</c>):
/// перерыв аккаунта кладёт на перерыв его пловца и наоборот.
/// </summary>
internal static class HubGroupBreakQuery
{
    public sealed record OnBreak(IReadOnlySet<int> UserIds, IReadOnlySet<int> SwimmerIds)
    {
        public static readonly OnBreak None = new(new HashSet<int>(), new HashSet<int>());
    }

    /// <param name="day">Календарный день по Израилю (сегодня или дата занятия/плана).</param>
    public static async Task<OnBreak> LoadAsync(SwimmDbContext db, int hubGroupId, DateOnly day)
    {
        // Условие «действует» — то же, что HubGroupBreakRules.IsActive, но в SQL.
        var rows = await db.HubGroupBreaks.AsNoTracking()
            .Where(b => b.HubGroupId == hubGroupId && b.EndedAt == null && (b.Until == null || b.Until >= day))
            .Select(b => new { b.UserId, b.SwimmerId })
            .ToListAsync();
        if (rows.Count == 0) return OnBreak.None;

        var users = rows.Where(r => r.UserId != null).Select(r => r.UserId!.Value).ToHashSet();
        var swimmers = rows.Where(r => r.SwimmerId != null).Select(r => r.SwimmerId!.Value).ToHashSet();

        var labels = await db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && m.SwimmerId != null)
            .Select(m => new { m.UserId, SwimmerId = m.SwimmerId!.Value })
            .ToListAsync();

        var linkedUsers = labels.Where(l => swimmers.Contains(l.SwimmerId)).Select(l => l.UserId).ToList();
        var linkedSwimmers = labels.Where(l => users.Contains(l.UserId)).Select(l => l.SwimmerId).ToList();
        users.UnionWith(linkedUsers);
        swimmers.UnionWith(linkedSwimmers);

        return new OnBreak(users, swimmers);
    }
}
