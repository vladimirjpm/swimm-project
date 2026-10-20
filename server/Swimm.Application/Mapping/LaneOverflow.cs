namespace Swimm.Application.Mapping;

/// <summary>
/// Перелив по подуровням (решение Влада 20.10.2026, docs/plans/lane-plans-plan.md «Подуровни»):
/// у плана есть лимит людей на дорожку, и когда дорожкам уровня мест не хватает, НИЖНИЙ подуровень
/// этого уровня («Advanced 2») по одному человеку переходит на дорожки СЛЕДУЮЩЕГО по силе уровня.
/// Только вниз, только на соседний блок дорожек; вверх никто не поднимается. Чистая функция, общая
/// для <see cref="LaneDistribution"/> и <see cref="LaneAllocation"/>.
///
/// Правила:
/// <list type="bullet">
/// <item>Лимита нет — перелива нет: люди просто режутся кусками по дорожкам своего блока.</item>
/// <item>Переходят только люди нижнего подуровня того уровня, который в блоке слабейший (у уровня
/// без подуровней и у пловца без подуровня перелива нет). Первым уходит последний в порядке.</item>
/// <item>Уходят, только если внизу есть место — с учётом того, что нижний блок и сам может отдать
/// своих дальше вниз (цепочка). Мест нет нигде — блок остаётся переполненным, тренер видит это в
/// редакторе.</item>
/// <item>Пришедшие сверху встают на дорожке первыми (они сильнее) и сами дальше не уходят.</item>
/// </list>
/// </summary>
public static class LaneOverflow
{
    /// <summary>
    /// Блок дорожек: номера по возрастанию и люди в порядке «сильнейший первым» (этот порядок —
    /// кто ведёт). <paramref name="Movable"/> — кто из них может уйти вниз (нижний подуровень).
    /// </summary>
    public sealed record Block(IReadOnlyList<int> LaneNos, IReadOnlyList<int> SwimmerIds, IReadOnlySet<int> Movable);

    /// <summary>
    /// Раскладка: (пловец, дорожка) по номеру дорожки, внутри — итоговый порядок дорожки (кто ведёт).
    /// Кого нет — не разложен (Unassigned).
    /// </summary>
    public static List<LaneDistribution.Placement> Place(IReadOnlyList<Block> blocks, int? maxPerLane)
    {
        var placed = new List<LaneDistribution.Placement>();
        var n = blocks.Count;

        // absorb[k] — сколько пришедших сверху блок k может принять, не переполнившись: свои
        // свободные места плюс тех своих, кого он сам может отдать вниз (absorb[k + 1]).
        var absorb = new int[n + 1];
        if (maxPerLane is int max)
            for (var k = n - 1; k >= 0; k--)
            {
                var capacity = max * blocks[k].LaneNos.Count;
                var canShed = Math.Min(MovableCount(blocks[k]), absorb[k + 1]);
                absorb[k] = Math.Max(0, capacity - blocks[k].SwimmerIds.Count + canShed);
            }

        var incoming = new List<int>();
        for (var k = 0; k < n; k++)
        {
            var block = blocks[k];
            var own = block.SwimmerIds.ToList();
            var leaving = new List<int>();
            if (maxPerLane is int cap)
            {
                var over = incoming.Count + own.Count - cap * block.LaneNos.Count;
                var shed = Math.Min(Math.Max(0, over), Math.Min(MovableCount(block), absorb[k + 1]));
                // С конца порядка: уходят слабейшие из нижнего подуровня.
                for (var i = own.Count - 1; i >= 0 && leaving.Count < shed; i--)
                    if (block.Movable.Contains(own[i])) leaving.Add(own[i]);
                leaving.Reverse();
                own.RemoveAll(leaving.Contains);
            }

            Cut([.. incoming, .. own], block.LaneNos, placed);
            incoming = leaving;
        }
        return placed.OrderBy(p => p.LaneNo).ToList();   // OrderBy устойчив — порядок внутри дорожки сохраняется
    }

    private static int MovableCount(Block block) => block.SwimmerIds.Count(block.Movable.Contains);

    /// <summary>Подряд идущие куски по дорожкам, размеры отличаются не больше чем на 1, большие — первыми.</summary>
    private static void Cut(List<int> members, IReadOnlyList<int> laneNos, List<LaneDistribution.Placement> placed)
    {
        if (laneNos.Count == 0) return;
        int size = members.Count / laneNos.Count, extra = members.Count % laneNos.Count, index = 0;
        for (var i = 0; i < laneNos.Count; i++)
            for (var k = 0; k < size + (i < extra ? 1 : 0); k++) placed.Add(new LaneDistribution.Placement(members[index++], laneNos[i]));
    }

    /// <summary>
    /// Порядок внутри уровня: подуровень (1 — первым, без подуровня — в конце), затем порядок состава.
    /// </summary>
    public static int SubLevelOrder(LaneDistribution.Swimmer s) => s.SubLevel ?? int.MaxValue;

    /// <summary>Пловец — нижний подуровень своего уровня (у уровня 2+ подуровня): он может уйти вниз.</summary>
    public static bool IsBottom(LaneDistribution.Swimmer s, IReadOnlyDictionary<int, int> subLevelsOf) =>
        s.LevelId is int id && s.SubLevel is int sub
        && subLevelsOf.TryGetValue(id, out var count) && count >= 2 && sub == count;
}
