using Swimm.Application.Mapping;
using Xunit;
using static Swimm.Application.Mapping.LanePlanPlacement;

namespace Swimm.Tests;

/// <summary>
/// Постановка одного человека при «Going» (docs/plans/trainings-coach-flow-plan.md §5.3, CF4) —
/// чистая функция <see cref="LanePlanPlacement.ForGoing"/>.
/// </summary>
public class LanePlanPlacementTests
{
    private const int Fast = 1, Mid = 2, Slow = 3;

    private static readonly List<LaneAllocation.Level> Levels =
        [new(Fast, 1), new(Mid, 2, SubLevels: 2), new(Slow, 3)];

    private static List<Lane> Lanes(params int?[] levels) => levels.Select((l, i) => new Lane(i + 1, l)).ToList();

    [Fact]
    public void NoLevel_Unassigned()
    {
        var t = ForGoing(3, Lanes(Fast, Mid, Slow), [], Levels, 100, null, null, null);
        Assert.Equal(new Target(null, false), t);
    }

    [Fact]
    public void OwnLevel_FewestPeople_TieLowestNumber()
    {
        var lanes = Lanes(Mid, Mid, Mid);
        Occupant[] on = [new(1, 1, Mid), new(2, 1, Mid), new(3, 2, Mid), new(4, 3, Mid)];
        Assert.Equal(new Target(2, false), ForGoing(3, lanes, on, Levels, 100, Mid, null, null));
    }

    [Fact]
    public void NoLaneOfLevel_JoinsMergedLaneWithOwnLevel_ElseUnassigned()
    {
        // Дорожка 2 подписана Fast, но на ней уже плывёт Mid (слитые уровни).
        var lanes = Lanes(Fast, Fast, null);
        Occupant[] on = [new(1, 2, Mid)];
        Assert.Equal(new Target(2, false), ForGoing(3, lanes, on, Levels, 100, Mid, null, null));
        Assert.Equal(new Target(null, false), ForGoing(3, lanes, on, Levels, 100, Slow, null, null));
    }

    [Fact]
    public void LaneBeyondCount_Ignored()
    {
        var lanes = Lanes(Fast, Mid, Mid);
        Assert.Equal(new Target(2, false), ForGoing(2, lanes, [], Levels, 100, Mid, null, null));
    }

    [Fact]
    public void Limit_PrefersLaneWithRoom()
    {
        var lanes = Lanes(Mid, Mid);
        Occupant[] on = [new(1, 1, Mid), new(2, 2, Mid), new(3, 2, Mid)];
        // Без лимита — та, где меньше; с лимитом 1 мест нет нигде — всё равно самая свободная.
        Assert.Equal(new Target(1, false), ForGoing(2, lanes, on, Levels, 100, Mid, 1, 2));
    }

    [Fact]
    public void Limit_Full_BottomSubLevel_OverflowsToNextLevel_First()
    {
        var lanes = Lanes(Mid, Slow);
        Occupant[] on = [new(1, 1, Mid), new(2, 1, Mid)];
        // Нижний подуровень Mid (2 из 2) — на дорожку Slow, первым.
        Assert.Equal(new Target(2, true), ForGoing(2, lanes, on, Levels, 100, Mid, 2, 2));
        // Верхний подуровень — остаётся на своей, дорожка переполняется.
        Assert.Equal(new Target(1, false), ForGoing(2, lanes, on, Levels, 100, Mid, 1, 2));
    }

    [Fact]
    public void Limit_Full_NextLevelFullToo_StaysOverfull()
    {
        var lanes = Lanes(Mid, Slow);
        Occupant[] on = [new(1, 1, Mid), new(2, 2, Slow)];
        Assert.Equal(new Target(1, false), ForGoing(2, lanes, on, Levels, 100, Mid, 2, 1));
    }

    [Fact]
    public void SelfNotCountedAsOccupant()
    {
        var lanes = Lanes(Mid, Mid);
        Occupant[] on = [new(100, 1, Mid), new(2, 2, Mid)];
        Assert.Equal(new Target(1, false), ForGoing(2, lanes, on, Levels, 100, Mid, null, null));
    }
}
