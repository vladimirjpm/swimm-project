namespace Swimm.Application.Mapping;

/// <summary>
/// «Distribute» плана дорожек (docs/plans/lane-plans-plan.md): пловцы каждого уровня
/// раскладываются по дорожкам этого уровня поровну. Чистая функция — без БД, детерминирована.
/// </summary>
public static class LaneDistribution
{
    public sealed record Lane(int LaneNo, int? LevelId);

    /// <summary>Пловец в порядке состава (<paramref name="SortKey"/> — место в этом порядке).</summary>
    public sealed record Swimmer(int SwimmerId, int? LevelId, int SortKey);

    public sealed record Placement(int SwimmerId, int? LaneNo);

    /// <summary>
    /// Для каждого уровня: его пловцы по <c>SortKey</c> режутся на подряд идущие куски по его
    /// дорожкам (по возрастанию номера), размеры кусков отличаются не больше чем на 1, большие —
    /// первыми. Кусками, а не «по кругу»: когда порядок станет «по скорости», быстрые окажутся
    /// вместе на меньшем номере дорожки, а не вперемешку.
    /// Без уровня или с уровнем без дорожек — <c>LaneNo = null</c> (Unassigned).
    /// Результат — в порядке <c>SortKey</c>; внутри дорожки этот же порядок = кто ведёт.
    /// </summary>
    public static IReadOnlyList<Placement> Distribute(IEnumerable<Lane> lanes, IEnumerable<Swimmer> swimmers)
    {
        var lanesByLevel = lanes
            .Where(l => l.LevelId != null)
            .GroupBy(l => l.LevelId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(l => l.LaneNo).Distinct().Order().ToList());

        var ordered = swimmers.OrderBy(s => s.SortKey).ThenBy(s => s.SwimmerId).ToList();
        var laneOf = new Dictionary<int, int?>();

        foreach (var group in ordered.Where(s => s.LevelId != null).GroupBy(s => s.LevelId!.Value))
        {
            if (!lanesByLevel.TryGetValue(group.Key, out var levelLanes)) continue;

            var members = group.ToList();
            int size = members.Count / levelLanes.Count, extra = members.Count % levelLanes.Count;
            var index = 0;
            for (var i = 0; i < levelLanes.Count; i++)
            {
                var take = size + (i < extra ? 1 : 0);
                for (var k = 0; k < take; k++) laneOf[members[index++].SwimmerId] = levelLanes[i];
            }
        }

        return ordered.Select(s => new Placement(s.SwimmerId, laneOf.GetValueOrDefault(s.SwimmerId))).ToList();
    }
}
