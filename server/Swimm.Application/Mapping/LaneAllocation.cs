namespace Swimm.Application.Mapping;

/// <summary>
/// «Auto lanes» плана дорожек (docs/plans/lane-plans-plan.md, решение Влада 26.09.2026): тренер
/// говорит только, сколько дорожек свободно, — функция сама делит их между уровнями по числу
/// пришедших и раскладывает людей. Чистая функция, без БД, детерминирована.
///
/// Правила:
/// <list type="bullet">
/// <item>Считаются только пришедшие с уровнем; без уровня — Unassigned.</item>
/// <item>Дорожек хватает на все уровни с людьми — каждому по одной, остальные по одной отдаются
/// уровню, у которого сейчас больше всего людей на дорожку (при равенстве — сильнейшему). Дорожек
/// у уровня не больше, чем людей: лишние остаются без уровня и пустыми.</item>
/// <item>Дорожек меньше, чем уровней, — соседние по силе уровни делят дорожку: разбиение на
/// подряд идущие группы, у которого самая многолюдная дорожка наименьшая; при равенстве сливаются
/// слабые, сильные остаются отдельно.</item>
/// <item>Сильные — на меньших номерах. Внутри группы дорожек люди режутся на подряд идущие куски,
/// как в <see cref="LaneDistribution"/>; на общей дорожке сперва сильнейший уровень.</item>
/// </list>
/// У дорожки в модели один уровень: общей дорожке подписью ставится сильнейший из слитых.
/// </summary>
public static class LaneAllocation
{
    public sealed record Level(int LevelId, int Rank);

    public sealed record Result(
        IReadOnlyList<LaneDistribution.Lane> Lanes, IReadOnlyList<LaneDistribution.Placement> Placements);

    /// <summary>Группа соседних уровней и сколько дорожек ей досталось.</summary>
    private sealed record Group(List<Level> Levels, int Count, int Lanes);

    public static Result AutoLanes(int laneCount, IEnumerable<Level> levels, IEnumerable<LaneDistribution.Swimmer> swimmers)
    {
        var people = swimmers.OrderBy(s => s.SortKey).ThenBy(s => s.SwimmerId).ToList();
        var countOf = people.Where(s => s.LevelId != null)
            .GroupBy(s => s.LevelId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        // Уровни с людьми, сильнейший первым. Уровень вне списка группы (удалили) — как без уровня.
        var present = levels
            .Where(l => countOf.ContainsKey(l.LevelId))
            .OrderBy(l => l.Rank).ThenBy(l => l.LevelId)
            .ToList();

        var groups = present.Count <= laneCount
            ? Spread(laneCount, present, countOf)
            : Merge(laneCount, present, countOf);

        var lanes = new List<LaneDistribution.Lane>(laneCount);
        var laneOf = new Dictionary<int, int?>();
        var rankOf = present.ToDictionary(l => l.LevelId, l => l.Rank);
        var next = 1;
        foreach (var g in groups)
        {
            var numbers = Enumerable.Range(next, g.Lanes).ToList();
            next += g.Lanes;
            numbers.ForEach(no => lanes.Add(new LaneDistribution.Lane(no, g.Levels[0].LevelId)));

            // Сперва сильнейший уровень группы, внутри — порядок состава.
            var ids = g.Levels.Select(l => l.LevelId).ToHashSet();
            var members = people.Where(s => s.LevelId is int id && ids.Contains(id))
                .OrderBy(s => rankOf[s.LevelId!.Value]).ToList();   // OrderBy устойчив — порядок состава сохраняется
            int size = members.Count / g.Lanes, extra = members.Count % g.Lanes, index = 0;
            for (var i = 0; i < g.Lanes; i++)
                for (var k = 0; k < size + (i < extra ? 1 : 0); k++) laneOf[members[index++].SwimmerId] = numbers[i];
        }
        for (; next <= laneCount; next++) lanes.Add(new LaneDistribution.Lane(next, null));

        return new Result(
            lanes,
            people.Select(s => new LaneDistribution.Placement(s.SwimmerId, laneOf.GetValueOrDefault(s.SwimmerId))).ToList());
    }

    /// <summary>Дорожек не меньше уровней: по одной каждому, остальные — самым загруженным.</summary>
    private static List<Group> Spread(int laneCount, List<Level> present, Dictionary<int, int> countOf)
    {
        var lanes = present.Select(_ => 1).ToArray();
        for (var left = laneCount - present.Count; left > 0; left--)
        {
            var best = -1;
            for (var i = 0; i < present.Count; i++)
            {
                var count = countOf[present[i].LevelId];
                if (lanes[i] >= count) continue;   // дорожек уже не меньше, чем людей
                // Больше всего людей на дорожку; строгое «больше» — при равенстве сильнейший (он раньше).
                if (best < 0 || (double)count / lanes[i] > (double)countOf[present[best].LevelId] / lanes[best]) best = i;
            }
            if (best < 0) break;                   // всем хватает — лишние дорожки пустые
            lanes[best]++;
        }
        return present.Select((l, i) => new Group([l], countOf[l.LevelId], lanes[i])).ToList();
    }

    /// <summary>
    /// Дорожек меньше уровней: подряд идущие группы, у которых самая многолюдная — наименьшая
    /// (динамика по префиксам; уровней не больше 12). При равенстве последняя группа — самая
    /// большая: сливаются слабые.
    /// </summary>
    private static List<Group> Merge(int laneCount, List<Level> present, Dictionary<int, int> countOf)
    {
        var n = present.Count;
        var prefix = new int[n + 1];
        for (var i = 0; i < n; i++) prefix[i + 1] = prefix[i] + countOf[present[i].LevelId];

        // best[j, i] — наименьший максимум, разбив первые i уровней на j групп; cut — начало последней.
        var best = new int[laneCount + 1, n + 1];
        var cut = new int[laneCount + 1, n + 1];
        for (var j = 0; j <= laneCount; j++)
            for (var i = 0; i <= n; i++) best[j, i] = int.MaxValue;
        best[0, 0] = 0;
        for (var j = 1; j <= laneCount; j++)
            for (var i = j; i <= n; i++)
                for (var p = j - 1; p < i; p++)    // по возрастанию p: при равенстве — самая длинная последняя группа
                {
                    if (best[j - 1, p] == int.MaxValue) continue;
                    var value = Math.Max(best[j - 1, p], prefix[i] - prefix[p]);
                    if (value < best[j, i]) { best[j, i] = value; cut[j, i] = p; }
                }

        var groups = new List<Group>(laneCount);
        for (int j = laneCount, i = n; j > 0; j--)
        {
            var p = cut[j, i];
            groups.Add(new Group(present.GetRange(p, i - p), prefix[i] - prefix[p], 1));
            i = p;
        }
        groups.Reverse();
        return groups;
    }
}
