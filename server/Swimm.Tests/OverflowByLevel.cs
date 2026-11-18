using Swimm.Application.Mapping;

namespace Swimm.Tests;

/// <summary>
/// Раскладка «уровень → его дорожки» поверх <see cref="LaneOverflow"/> — для тестов правил перелива
/// по подуровням (docs/plans/lane-plans-plan.md «Подуровни»). Была рабочей ручкой «Distribute»;
/// с CF8 плана trainings-coach-flow её заменил Auto-arrange (<see cref="LaneAllocation"/>), а сами
/// правила перелива общие — их спецификация живёт здесь.
///
/// Для каждого уровня его пловцы (подуровень 1 первым, затем по <c>SortKey</c>) режутся на подряд
/// идущие куски по дорожкам уровня; уровни по силе из <paramref name="levels"/> (нет их — по первому
/// номеру дорожки). Без уровня или с уровнем без дорожек — Unassigned, в конце.
/// </summary>
internal static class OverflowByLevel
{
    public static IReadOnlyList<LaneDistribution.Placement> Distribute(
        IEnumerable<LaneDistribution.Lane> lanes, IEnumerable<LaneDistribution.Swimmer> swimmers,
        IEnumerable<LaneAllocation.Level>? levels = null, int? maxPerLane = null)
    {
        var lanesByLevel = lanes
            .Where(l => l.LevelId != null)
            .GroupBy(l => l.LevelId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(l => l.LaneNo).Distinct().Order().ToList());
        var levelList = levels?.ToList() ?? [];
        var rankOf = levelList.ToDictionary(l => l.LevelId, l => l.Rank);
        var subLevelsOf = levelList.ToDictionary(l => l.LevelId, l => l.SubLevels);

        var ordered = swimmers.OrderBy(s => s.SortKey).ThenBy(s => s.SwimmerId).ToList();

        var blocks = lanesByLevel
            .OrderBy(kv => rankOf.TryGetValue(kv.Key, out var rank) ? rank : int.MaxValue)
            .ThenBy(kv => kv.Value[0])
            .Select(kv =>
            {
                var members = ordered.Where(s => s.LevelId == kv.Key)
                    .OrderBy(LaneOverflow.SubLevelOrder).ToList();
                return new LaneOverflow.Block(
                    kv.Value,
                    members.Select(s => s.SwimmerId).ToList(),
                    members.Where(s => LaneOverflow.IsBottom(s, subLevelsOf)).Select(s => s.SwimmerId).ToHashSet());
            })
            .ToList();

        var placed = LaneOverflow.Place(blocks, maxPerLane);
        var onLane = placed.Select(p => p.SwimmerId).ToHashSet();
        return [.. placed, .. ordered.Where(s => !onLane.Contains(s.SwimmerId)).Select(s => new LaneDistribution.Placement(s.SwimmerId, null))];
    }
}
