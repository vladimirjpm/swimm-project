using Microsoft.EntityFrameworkCore;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Запись ленты «Since publishing» (docs/plans/trainings-coach-flow-plan.md, CF8): события пишутся
/// только у ОПУБЛИКОВАННОГО плана, в том же сохранении, что и сама правка (вызывающий зовёт
/// <c>SaveChangesAsync</c>). Правила склейки — <see cref="LanePlanEventRules"/>.
/// </summary>
internal static class LanePlanEventLog
{
    /// <summary>Описание события: что, кто, ответил ли тренер за человека.</summary>
    public sealed record Source(string Kind, int? ActorUserId, bool ByCoach = false);

    /// <summary>
    /// План стал опубликованным: лента начинается заново — старое стирается, первая строка —
    /// «опубликован, N на дорожках». План должен быть уже сохранён (есть Id).
    /// </summary>
    public static async Task StartAsync(SwimmDbContext db, LanePlan plan, int? actorUserId, DateTime now)
    {
        await ClearAsync(db, plan.Id);
        db.LanePlanEvents.Add(new LanePlanEvent
        {
            PlanId = plan.Id, At = now, Kind = LanePlanEventKind.Published, ActorUserId = actorUserId,
            OnLanes = plan.Swimmers.Count(s => s.LaneNo is int no && no >= 1 && no <= plan.LaneCount),
        });
    }

    /// <summary>Unpublish: ленты больше нет (участники плана не видят — сообщать нечего).</summary>
    public static async Task ClearAsync(SwimmDbContext db, int planId)
    {
        var rows = await db.LanePlanEvents.Where(e => e.PlanId == planId).ToListAsync();
        db.LanePlanEvents.RemoveRange(rows);
    }

    /// <summary>
    /// Человек сменил место в опубликованном плане (<paramref name="from"/> → <paramref name="to"/>,
    /// числа — <see cref="LanePlanEventRules.Place(bool, int?)"/>). Черновик и «место не сменилось» —
    /// ничего. Перенос тренером склеивается с его же недавним переносом этого человека.
    /// </summary>
    public static async Task RecordAsync(SwimmDbContext db, LanePlan plan, int swimmerId, int? from, int? to,
        Source source, DateTime now)
    {
        if (plan.Status != LanePlanStatus.Published || from == to) return;

        if (source.Kind == LanePlanEventKind.Moved)
        {
            var last = await LastOfSwimmerAsync(db, plan.Id, swimmerId);
            switch (LanePlanEventRules.MergeCoachMove(last, source.ActorUserId, to, now))
            {
                case LanePlanEventRules.MoveMerge.Remove:
                    db.LanePlanEvents.Remove(last!);
                    return;
                case LanePlanEventRules.MoveMerge.Update:
                    last!.ToLane = to;
                    last.At = now;
                    return;
            }
        }

        db.LanePlanEvents.Add(new LanePlanEvent
        {
            PlanId = plan.Id, At = now, Kind = source.Kind, SwimmerId = swimmerId,
            FromLane = from, ToLane = to, ActorUserId = source.ActorUserId, ByCoach = source.ByCoach,
        });
    }

    /// <summary>
    /// Undo тоста тренера: последняя строка этого человека отменяется вместе со сдвигом (ответ и
    /// место вернулись — в ленте не должно остаться ни сдвига, ни «возврата»).
    /// </summary>
    public static async Task UndoLastAsync(SwimmDbContext db, int planId, int swimmerId)
    {
        var last = await LastOfSwimmerAsync(db, planId, swimmerId);
        if (last != null) db.LanePlanEvents.Remove(last);
    }

    private static Task<LanePlanEvent?> LastOfSwimmerAsync(SwimmDbContext db, int planId, int swimmerId) =>
        db.LanePlanEvents
            .Where(e => e.PlanId == planId && e.SwimmerId == swimmerId)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .FirstOrDefaultAsync();
}
