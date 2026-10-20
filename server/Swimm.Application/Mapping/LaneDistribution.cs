namespace Swimm.Application.Mapping;

/// <summary>
/// «Distribute» плана дорожек (docs/plans/lane-plans-plan.md): пловцы каждого уровня
/// раскладываются по дорожкам этого уровня поровну. Чистая функция — без БД, детерминирована.
/// </summary>
public static class LaneDistribution
{
    public sealed record Lane(int LaneNo, int? LevelId);

    /// <summary>
    /// Пловец в порядке состава (<paramref name="SortKey"/> — место в этом порядке);
    /// <paramref name="SubLevel"/> — подуровень внутри уровня (1 — верхний), null — не задан.
    /// </summary>
    public sealed record Swimmer(int SwimmerId, int? LevelId, int SortKey, int? SubLevel = null);

    public sealed record Placement(int SwimmerId, int? LaneNo);

    /// <summary>
    /// Для каждого уровня: его пловцы (подуровень 1 первым, затем по <c>SortKey</c>) режутся на
    /// подряд идущие куски по его дорожкам (по возрастанию номера), размеры кусков отличаются не
    /// больше чем на 1, большие — первыми. Кусками, а не «по кругу»: когда порядок станет «по
    /// скорости», быстрые окажутся вместе на меньшем номере дорожки, а не вперемешку.
    /// Без уровня или с уровнем без дорожек — <c>LaneNo = null</c> (Unassigned).
    /// <paramref name="maxPerLane"/> задан — перелив по подуровням вниз (<see cref="LaneOverflow"/>):
    /// уровни по силе из <paramref name="levels"/> (нет их — по первому номеру дорожки).
    /// Результат — по дорожкам (по номеру), внутри — порядок дорожки (кто ведёт); в конце
    /// Unassigned в порядке <c>SortKey</c>.
    /// </summary>
    public static IReadOnlyList<Placement> Distribute(
        IEnumerable<Lane> lanes, IEnumerable<Swimmer> swimmers,
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

        // Блок — дорожки одного уровня, сильнейший уровень первым. От дорожек, а не от людей:
        // на дорожку уровня, у которого сегодня никого нет, тоже можно перелиться.
        var blocks = lanesByLevel
            .OrderBy(kv => rankOf.TryGetValue(kv.Key, out var rank) ? rank : int.MaxValue)
            .ThenBy(kv => kv.Value[0])
            .Select(kv =>
            {
                var members = ordered.Where(s => s.LevelId == kv.Key)
                    .OrderBy(LaneOverflow.SubLevelOrder).ToList();   // OrderBy устойчив — порядок состава внутри
                return new LaneOverflow.Block(
                    kv.Value,
                    members.Select(s => s.SwimmerId).ToList(),
                    members.Where(s => LaneOverflow.IsBottom(s, subLevelsOf)).Select(s => s.SwimmerId).ToHashSet());
            })
            .ToList();

        var placed = LaneOverflow.Place(blocks, maxPerLane);
        var onLane = placed.Select(p => p.SwimmerId).ToHashSet();
        return [.. placed, .. ordered.Where(s => !onLane.Contains(s.SwimmerId)).Select(s => new Placement(s.SwimmerId, null))];
    }
}
