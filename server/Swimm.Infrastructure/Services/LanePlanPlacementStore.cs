using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Ответ сдвигает ОДНОГО пловца в плане дорожек занятия (docs/plans/trainings-coach-flow-plan.md §5.3,
/// CF4; с CF5 план — у занятия): «Going» ставит на дорожку его уровня (<see cref="LanePlanPlacement"/>),
/// «Can't come» снимает в «Not today», Undo тоста возвращает на прежнее место. Работает и в черновике,
/// и в опубликованном плане; плана у занятия нет — ничего не делает (вид по дорожкам «auto» считает сам).
///
/// Под тем же advisory-lock группы, что и сохранение плана (<see cref="LanePlanService"/>): иначе
/// сохранение редактора и ответ в одну секунду переписали бы друг друга посередине.
/// </summary>
internal static class LanePlanPlacementStore
{
    public enum Intent { Going, Out }

    /// <summary>
    /// Применить правило ответа. null — сдвига нет (плана нет, уже на дорожке, …). У опубликованного
    /// плана сдвиг уходит строкой в ленту «Since publishing» (CF8) с описанием <paramref name="source"/>.
    /// </summary>
    public static Task<LaneChangeDto?> ApplyAsync(SwimmDbContext db, int hubGroupId, LanePlanKey key, int swimmerId, Intent intent,
        LanePlanEventLog.Source source) =>
        RunAsync(db, hubGroupId, key, swimmerId,
            (plan, before, after, now) => LanePlanEventLog.RecordAsync(db, plan, swimmerId,
                LanePlanEventRules.Place(before), LanePlanEventRules.Place(after), source, now),
            async plan =>
        {
            var row = plan.Swimmers.FirstOrDefault(s => s.SwimmerId == swimmerId);
            if (intent == Intent.Out)
            {
                // План — у занятия (CF5): ответ на другое занятие того же дня этот план не держит.
                if (row == null) return false;
                plan.Swimmers.Remove(row);
                Renumber(plan, row.LaneNo);
                return true;
            }

            // Уже на видимой дорожке — тренер (или прошлый ответ) поставил, не трогаем.
            if (row?.LaneNo is int at && at >= 1 && at <= plan.LaneCount) return false;

            var level = await db.HubGroupSwimmerLevels.AsNoTracking()
                .Where(l => l.HubGroupId == hubGroupId && l.SwimmerId == swimmerId)
                .Select(l => new { l.LevelId, l.SubLevel })
                .FirstOrDefaultAsync();
            var groupLevels = await db.HubGroupLevels.AsNoTracking()
                .Where(l => l.HubGroupId == hubGroupId)
                .Select(l => new LaneAllocation.Level(l.Id, l.Rank, l.SubLevels))
                .ToListAsync();
            var onLanes = plan.Swimmers.Where(s => s.LaneNo != null).Select(s => s.SwimmerId).ToList();
            var levelOf = await db.HubGroupSwimmerLevels.AsNoTracking()
                .Where(l => l.HubGroupId == hubGroupId && onLanes.Contains(l.SwimmerId))
                .ToDictionaryAsync(l => l.SwimmerId, l => l.LevelId);

            var target = LanePlanPlacement.ForGoing(
                plan.LaneCount,
                plan.Lanes.Select(l => new LanePlanPlacement.Lane(l.LaneNo, l.LevelId)).ToList(),
                plan.Swimmers.Where(s => s.LaneNo != null)
                    .Select(s => new LanePlanPlacement.Occupant(s.SwimmerId, s.LaneNo!.Value,
                        levelOf.TryGetValue(s.SwimmerId, out var lv) ? lv : null))
                    .ToList(),
                groupLevels, swimmerId, level?.LevelId, level?.SubLevel, plan.MaxPerLane);

            // Без уровня и уже в Unassigned — некуда двигать.
            if (row != null && target.LaneNo == null && row.LaneNo == null) return false;

            var from = row?.LaneNo;
            if (row == null)
            {
                row = new LanePlanSwimmer { SwimmerId = swimmerId };
                plan.Swimmers.Add(row);
            }
            Insert(plan, row, target.LaneNo, target.First ? 0 : int.MaxValue);
            row.Pinned = false;
            Renumber(plan, from);
            return true;
        });

    /// <summary>
    /// Undo: вернуть пловца на место <paramref name="place"/> (InPlan false — «Not today»). Строка ленты
    /// об отменённом сдвиге уходит вместе с ним (CF8).
    /// </summary>
    public static Task<LaneChangeDto?> RestoreAsync(SwimmDbContext db, int hubGroupId, LanePlanKey key, int swimmerId, LanePlaceDto place) =>
        RunAsync(db, hubGroupId, key, swimmerId,
            (plan, _, _, _) => plan.Status == LanePlanStatus.Published
                ? LanePlanEventLog.UndoLastAsync(db, plan.Id, swimmerId)
                : Task.CompletedTask,
            async plan =>
        {
            var row = plan.Swimmers.FirstOrDefault(s => s.SwimmerId == swimmerId);
            if (!place.InPlan)
            {
                if (row == null) return false;
                plan.Swimmers.Remove(row);
                Renumber(plan, row.LaneNo);
                return true;
            }

            // Пловец — из видимого состава или уже в плане (как при сохранении плана).
            if (row == null && !await db.HubGroupMembers.AnyAsync(m =>
                    m.HubGroupId == hubGroupId && m.SwimmerId == swimmerId && !m.IsExcluded))
                return false;
            var lane = place.LaneNo is int no && no >= 1 && no <= LanePlanRules.MaxLanes ? no : (int?)null;
            var from = row?.LaneNo;
            if (row == null)
            {
                row = new LanePlanSwimmer { SwimmerId = swimmerId };
                plan.Swimmers.Add(row);
            }
            Insert(plan, row, lane, Math.Max(0, place.OrderNo));
            row.Pinned = lane != null && place.Pinned;
            Renumber(plan, from);
            return true;
        });

    private static async Task<LaneChangeDto?> RunAsync(SwimmDbContext db, int hubGroupId, LanePlanKey key, int swimmerId,
        Func<LanePlan, LanePlaceDto, LanePlaceDto, DateTime, Task> log, Func<LanePlan, Task<bool>> change) =>
        await GroupAdvisoryLock.InGroupTransactionAsync<LaneChangeDto?>(db, LanePlanService.LanePlansLockClass, hubGroupId, async () =>
        {
            var (date, start) = (key.Date, key.Start);
            var plan = await db.LanePlans
                .Include(p => p.Lanes)
                .Include(p => p.Swimmers)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.HubGroupId == hubGroupId && p.Date == date && p.Start == start);
            if (plan == null) return null;

            var before = PlaceOf(plan, swimmerId);
            if (!await change(plan)) return null;
            var after = PlaceOf(plan, swimmerId);
            if (Same(before, after)) return null;

            var now = DateTime.UtcNow;
            plan.UpdatedAt = now;
            await log(plan, before, after, now);
            await db.SaveChangesAsync();
            return new LaneChangeDto { SwimmerId = swimmerId, Before = before, After = after };
        });

    private static LanePlaceDto PlaceOf(LanePlan plan, int swimmerId)
    {
        var row = plan.Swimmers.FirstOrDefault(s => s.SwimmerId == swimmerId);
        return row == null
            ? new LanePlaceDto()
            : new LanePlaceDto { InPlan = true, LaneNo = row.LaneNo, OrderNo = row.OrderNo, Pinned = row.Pinned };
    }

    private static bool Same(LanePlaceDto a, LanePlaceDto b) =>
        a.InPlan == b.InPlan && a.LaneNo == b.LaneNo && a.OrderNo == b.OrderNo && a.Pinned == b.Pinned;

    /// <summary>Поставить <paramref name="row"/> в корзину <paramref name="laneNo"/> на место <paramref name="at"/> (int.MaxValue — в конец).</summary>
    private static void Insert(LanePlan plan, LanePlanSwimmer row, int? laneNo, int at)
    {
        var list = plan.Swimmers
            .Where(s => s != row && s.LaneNo == laneNo)
            .OrderBy(s => s.OrderNo).ThenBy(s => s.SwimmerId)
            .ToList();
        list.Insert(Math.Min(at, list.Count), row);
        row.LaneNo = laneNo;
        for (var i = 0; i < list.Count; i++) list[i].OrderNo = i;
    }

    /// <summary>Номера мест в корзине без дыр (0, 1, 2…), как присылает редактор.</summary>
    private static void Renumber(LanePlan plan, int? laneNo)
    {
        var i = 0;
        foreach (var s in plan.Swimmers.Where(s => s.LaneNo == laneNo).OrderBy(s => s.OrderNo).ThenBy(s => s.SwimmerId))
            s.OrderNo = i++;
    }
}
