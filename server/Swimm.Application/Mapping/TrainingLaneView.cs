using Swimm.Application.Dtos;
using Swimm.Domain;

namespace Swimm.Application.Mapping;

/// <summary>
/// Вид по дорожкам на занятие (docs/plans/entity-hero-roles-plan.md §5, Ш3.2) — чистая функция,
/// без БД, детерминирована. Кто пришёл и кем он стоит (пловцом или аккаунтом) решает вызывающий
/// (резолвер «кто на дорожке»), здесь — только раскладка.
///
/// Источник дорожек по порядку (решение Влада 28.09.2026):
/// <list type="number">
/// <item>опубликованный план тренера на дату — как есть;</item>
/// <item>плана нет — раскладка на лету <see cref="LaneAllocation.AutoLanes"/> по уровням на
/// «Usual lanes» (иначе число дорожек последнего плана); соседние уровни сливаются при нехватке;</item>
/// <item>уровней нет — те же дорожки, люди подряд идущими кусками в порядке состава;</item>
/// <item>число дорожек неизвестно — одна общая «вода».</item>
/// </list>
/// Режим группы off — вида нет; plan — только план.
/// </summary>
public static class TrainingLaneView
{
    public const string SourcePlan = "plan";
    public const string SourceAuto = "auto";
    public const string SourceWater = "water";

    /// <summary>
    /// Человек в бассейне (ответил «иду» или «не уверен»). <paramref name="SortKey"/> — место в
    /// порядке состава (аккаунты без пловца — после состава); <paramref name="Claims"/> — сколько
    /// аккаунтов группы называют себя тем же пловцом.
    /// </summary>
    public sealed record Person(
        int UserId, int? SwimmerId, string Name, string? Gender, string Answer, string? Note,
        int? LevelId, int SortKey, int Claims);

    public sealed record LevelInfo(int Id, int Rank, string Name, string? Color);

    public sealed record PlanLane(int LaneNo, int? LevelId, string? Workout);

    /// <summary>Опубликованный план на дату: дорожки и места пловцов (LaneNo null — Unassigned).</summary>
    public sealed record Plan(int LaneCount, IReadOnlyList<PlanLane> Lanes,
        IReadOnlyDictionary<int, (int? LaneNo, int OrderNo)> Places);

    public static TrainingLaneViewDto? Build(
        string laneView, Plan? plan, int? usualLanes, int? lastPlanLaneCount,
        IReadOnlyList<LevelInfo> levels, IReadOnlyList<Person> pool, int viewerUserId, bool namesVisible)
    {
        if (laneView == GroupLaneView.Off) return null;
        if (plan == null && laneView == GroupLaneView.Plan) return null;

        var levelById = levels.ToDictionary(l => l.Id);
        var people = pool.OrderBy(p => p.SortKey).ThenBy(p => p.UserId).ToList();

        TrainingLanePersonDto ToDto(Person p)
        {
            var isMe = p.UserId == viewerUserId;
            var shown = namesVisible || isMe;
            return new TrainingLanePersonDto
            {
                UserId = shown ? p.UserId : null,
                SwimmerId = shown ? p.SwimmerId : null,
                Name = shown ? p.Name : null,
                Gender = shown && p.Gender is "male" or "female" ? p.Gender : null,
                Answer = p.Answer,
                Note = shown ? p.Note : null,
                IsMe = isMe,
                // «2 claim» — сигнал тренеру; при скрытых именах он выдал бы чужую заявку даже в своём кружке.
                Claims = namesVisible ? p.Claims : 1,
            };
        }

        LanePlanLevelDto? LevelDto(int? levelId) =>
            levelId is int id && levelById.TryGetValue(id, out var l)
                ? new LanePlanLevelDto { Id = l.Id, Rank = l.Rank, Name = l.Name, Color = l.Color }
                : null;

        var dto = new TrainingLaneViewDto { NamesHidden = !namesVisible };

        if (plan != null)
        {
            dto.Source = SourcePlan;
            dto.LaneCount = plan.LaneCount;
            var placed = new Dictionary<int, List<(Person P, int Order)>>();
            foreach (var p in people)
            {
                if (p.SwimmerId is int sid && plan.Places.TryGetValue(sid, out var place)
                    && place.LaneNo is int no && no <= plan.LaneCount)
                {
                    if (!placed.TryGetValue(no, out var list)) placed[no] = list = [];
                    list.Add((p, place.OrderNo));
                }
                else dto.NoLane.Add(ToDto(p));
            }
            dto.Lanes = plan.Lanes
                .Where(l => l.LaneNo <= plan.LaneCount)
                .OrderBy(l => l.LaneNo)
                .Select(l => new TrainingLaneDto
                {
                    LaneNo = l.LaneNo,
                    Level = LevelDto(l.LevelId),
                    Workout = l.Workout,
                    People = placed.TryGetValue(l.LaneNo, out var list)
                        ? list.OrderBy(x => x.Order).ThenBy(x => x.P.SortKey).Select(x => ToDto(x.P)).ToList()
                        : [],
                })
                .ToList();
            return dto;
        }

        var laneCount = usualLanes ?? lastPlanLaneCount;
        if (laneCount is not int count || count < LanePlanRules.MinLanes)
        {
            dto.Source = SourceWater;
            dto.LaneCount = 1;
            dto.Lanes = [new TrainingLaneDto { LaneNo = 1, People = people.Select(ToDto).ToList() }];
            return dto;
        }
        count = Math.Min(count, LanePlanRules.MaxLanes);

        dto.Source = SourceAuto;
        dto.LaneCount = count;

        // Уровни в ходу, если хоть у кого-то из пришедших уровень этой группы; тогда без уровня —
        // «No lane» (тренеру видно, кому поставить уровень). Иначе — один общий «уровень»-заглушка:
        // люди подряд идущими кусками по всем дорожкам в порядке состава.
        const int NoLevel = int.MinValue;
        var levelsInUse = people.Any(p => p.LevelId is int id && levelById.ContainsKey(id));
        var allocation = LaneAllocation.AutoLanes(
            count,
            levelsInUse ? levels.Select(l => new LaneAllocation.Level(l.Id, l.Rank)) : [new LaneAllocation.Level(NoLevel, 1)],
            people.Select((p, i) => new LaneDistribution.Swimmer(
                i,
                levelsInUse ? (p.LevelId is int id && levelById.ContainsKey(id) ? id : null) : NoLevel,
                p.SortKey)));

        var laneOfIndex = allocation.Placements.ToDictionary(x => x.SwimmerId, x => x.LaneNo);
        var levelOfLane = allocation.Lanes.ToDictionary(l => l.LaneNo, l => l.LevelId);
        for (var i = 0; i < people.Count; i++)
            if (laneOfIndex.GetValueOrDefault(i) == null) dto.NoLane.Add(ToDto(people[i]));

        dto.Lanes = Enumerable.Range(1, count)
            .Select(no => new TrainingLaneDto
            {
                LaneNo = no,
                Level = LevelDto(levelOfLane.GetValueOrDefault(no) is int id && id != NoLevel ? id : null),
                People = people.Where((_, i) => laneOfIndex.GetValueOrDefault(i) == no).Select(ToDto).ToList(),
            })
            .ToList();
        return dto;
    }
}
