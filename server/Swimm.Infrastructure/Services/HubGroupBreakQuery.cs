using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Кто в группе «не в воде» в данный день (Ш3.1; с 30.09.2026 — и «Doesn't train», Р74) — одно
/// место для RSVP, плана дорожек и карточек: иначе «знаменатель полосы» и «кто не лезет в
/// Unassigned» разъехались бы.
///
/// Флаг один на человека (решение Влада 28.09.2026), а строк у человека может быть две —
/// у аккаунта и у его пловца. Связка — <see cref="HubGroupPersonResolver"/> (метка тренера,
/// привязка админом, «Me», «Family»): флаг аккаунта кладёт в тот же статус его пловца и наоборот.
/// </summary>
internal static class HubGroupBreakQuery
{
    /// <summary>
    /// <see cref="UserIds"/> / <see cref="SwimmerIds"/> — ВСЕ «не в воде» (перерыв и «не
    /// тренируется»): их не считают в знаменатель и не кладут в Unassigned. <see cref="NotTrainingUserIds"/> /
    /// <see cref="NotTrainingSwimmerIds"/> — подмножество «не тренируется с группой» (ответ
    /// «Going» его не снимает, в списке тренера — своя группа).
    /// </summary>
    public sealed record OnBreak(
        IReadOnlySet<int> UserIds, IReadOnlySet<int> SwimmerIds,
        IReadOnlySet<int> NotTrainingUserIds, IReadOnlySet<int> NotTrainingSwimmerIds)
    {
        public static readonly OnBreak None = new(
            new HashSet<int>(), new HashSet<int>(), new HashSet<int>(), new HashSet<int>());
    }

    /// <param name="day">Календарный день по Израилю (сегодня или дата занятия/плана).</param>
    public static async Task<OnBreak> LoadAsync(SwimmDbContext db, int hubGroupId, DateOnly day)
    {
        // Условие «действует» — то же, что HubGroupBreakRules.IsActive, но в SQL.
        var rows = await db.HubGroupBreaks.AsNoTracking()
            .Where(b => b.HubGroupId == hubGroupId && b.EndedAt == null && (b.Until == null || b.Until >= day))
            .Select(b => new { b.UserId, b.SwimmerId, b.Kind })
            .ToListAsync();
        if (rows.Count == 0) return OnBreak.None;

        // Связка «аккаунт ↔ его пловец» — тот же резолвер, что у вида по дорожкам (метка тренера,
        // привязка админом, «Me», «Family»): кто стоит на дорожке пловцом X, тот и в статусе X.
        var memberIds = await db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId)
            .Select(m => m.UserId)
            .ToListAsync();
        var resolved = await HubGroupPersonResolver.ResolveAsync(db, hubGroupId, memberIds);
        var links = resolved.Where(r => r.Value != null).Select(r => (UserId: r.Key, SwimmerId: r.Value!.Value)).ToList();

        (HashSet<int> Users, HashSet<int> Swimmers) Expand(IEnumerable<(int? UserId, int? SwimmerId)> subset)
        {
            var list = subset.ToList();
            var users = list.Where(r => r.UserId != null).Select(r => r.UserId!.Value).ToHashSet();
            var swimmers = list.Where(r => r.SwimmerId != null).Select(r => r.SwimmerId!.Value).ToHashSet();
            var linkedUsers = links.Where(l => swimmers.Contains(l.SwimmerId)).Select(l => l.UserId).ToList();
            var linkedSwimmers = links.Where(l => users.Contains(l.UserId)).Select(l => l.SwimmerId).ToList();
            users.UnionWith(linkedUsers);
            swimmers.UnionWith(linkedSwimmers);
            return (users, swimmers);
        }

        var all = Expand(rows.Select(r => (r.UserId, r.SwimmerId)));
        var notTraining = Expand(rows.Where(r => r.Kind == HubGroupBreakKind.NotTraining).Select(r => (r.UserId, r.SwimmerId)));
        return new OnBreak(all.Users, all.Swimmers, notTraining.Users, notTraining.Swimmers);
    }
}
