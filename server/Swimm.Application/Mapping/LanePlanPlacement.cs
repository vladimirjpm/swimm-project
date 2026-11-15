namespace Swimm.Application.Mapping;

/// <summary>
/// Постановка ОДНОГО человека на дорожку при ответе «Going» (docs/plans/trainings-coach-flow-plan.md
/// §5.3, CF4). Чистая функция, без БД, детерминирована. Расстановку остальных не трогает — в отличие
/// от <see cref="LaneAllocation.AutoLanes"/>, которая раскладывает всех заново.
///
/// Правила:
/// <list type="bullet">
/// <item>Уровня нет (или уровень удалён из группы) — Unassigned (<c>null</c>).</item>
/// <item>Кандидаты — видимые дорожки его уровня. Таких нет — дорожки, где уже стоит кто-то его уровня
/// (общая дорожка слитых уровней подписана сильнейшим, <see cref="LaneAllocation"/>). Нет и таких —
/// Unassigned: куда ставить, решает тренер.</item>
/// <item>Из кандидатов — где меньше людей; при равенстве — меньший номер. Встаёт в конец дорожки.</item>
/// <item>Лимит «Max per lane»: сперва кандидаты с местом. Мест нет — перелив по нынешнему правилу
/// (<see cref="LaneOverflow"/>): нижний подуровень уходит на дорожку СЛЕДУЮЩЕГО по силе уровня, если
/// там есть место, и встаёт на ней первым (он сильнее). Иначе — на самую свободную из своих:
/// дорожка переполняется, тренер видит это в редакторе и в «Needs you».</item>
/// </list>
/// </summary>
public static class LanePlanPlacement
{
    /// <summary>Дорожка плана: номер и уровень (подпись).</summary>
    public sealed record Lane(int LaneNo, int? LevelId);

    /// <summary>Кто уже стоит на дорожке и какого он уровня (null — без уровня).</summary>
    public sealed record Occupant(int SwimmerId, int LaneNo, int? LevelId);

    /// <summary>Куда встать: <paramref name="LaneNo"/> null — Unassigned; <paramref name="First"/> — в начало дорожки.</summary>
    public sealed record Target(int? LaneNo, bool First);

    public static Target ForGoing(
        int laneCount, IReadOnlyList<Lane> lanes, IReadOnlyList<Occupant> occupants,
        IReadOnlyList<LaneAllocation.Level> groupLevels, int swimmerId, int? levelId, int? subLevel, int? maxPerLane)
    {
        var unassigned = new Target(null, false);
        var level = levelId is int id ? groupLevels.FirstOrDefault(l => l.LevelId == id) : null;
        if (level == null) return unassigned;

        var visible = lanes.Where(l => l.LaneNo >= 1 && l.LaneNo <= laneCount).OrderBy(l => l.LaneNo).ToList();
        var others = occupants.Where(o => o.SwimmerId != swimmerId && o.LaneNo >= 1 && o.LaneNo <= laneCount).ToList();
        int CountOn(int laneNo) => others.Count(o => o.LaneNo == laneNo);
        bool HasRoom(int laneNo) => maxPerLane is not int max || CountOn(laneNo) < max;
        int? Freest(IEnumerable<int> laneNos) => laneNos
            .OrderBy(CountOn).ThenBy(no => no)
            .Select(no => (int?)no)
            .FirstOrDefault();

        var own = visible.Where(l => l.LevelId == level.LevelId).Select(l => l.LaneNo).ToList();
        if (own.Count == 0)
            own = others.Where(o => o.LevelId == level.LevelId).Select(o => o.LaneNo).Distinct().ToList();
        if (own.Count == 0) return unassigned;

        var roomy = own.Where(HasRoom).ToList();
        if (roomy.Count > 0) return new Target(Freest(roomy), false);

        // Лимит есть и свои дорожки полны: нижний подуровень — на дорожку следующего уровня, если там место.
        var bottom = LaneOverflow.IsBottom(
            new LaneDistribution.Swimmer(swimmerId, level.LevelId, 0, subLevel),
            groupLevels.ToDictionary(l => l.LevelId, l => l.SubLevels));
        if (bottom)
        {
            var weaker = groupLevels
                .Where(l => l.Rank > level.Rank || (l.Rank == level.Rank && l.LevelId > level.LevelId))
                .OrderBy(l => l.Rank).ThenBy(l => l.LevelId)
                .Select(l => l.LevelId)
                .FirstOrDefault(next => visible.Any(v => v.LevelId == next));
            if (weaker != 0)
            {
                var down = visible.Where(l => l.LevelId == weaker && HasRoom(l.LaneNo)).Select(l => l.LaneNo).ToList();
                if (down.Count > 0) return new Target(Freest(down), true);
            }
        }

        return new Target(Freest(own), false);
    }
}
