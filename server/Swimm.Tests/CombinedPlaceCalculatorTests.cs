using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты объединённого зачёта «Combine All Results» (<see cref="CombinedPlaceCalculator"/>) —
/// серверного порта клиентского recalculatePositions. Образец — PointRulesClubsScoringTests.
/// </summary>
public class CombinedPlaceCalculatorTests
{
    private const string Free50 = "1|50|50m|male|";
    private const string Back50 = "2|50|50m|male|";

    private static CombinedPlaceCalculator.Row Row(
        long id, int swimmerId, int? timeMs, string eventKey = Free50, bool timeFail = false)
        => new(id, swimmerId, eventKey, timeMs, timeFail);

    private static CombinedPlaceCalculator.Assignment For(
        IEnumerable<CombinedPlaceCalculator.Assignment> all, long resultId)
        => all.First(a => a.ResultId == resultId);

    [Fact]
    public void RanksSwimmersByTime_AcrossDays()
    {
        // Три пловца в одной дисциплине, заплывы в разные дни события.
        var result = CombinedPlaceCalculator.Calculate([
            Row(1, swimmerId: 10, timeMs: 31_000),
            Row(2, swimmerId: 20, timeMs: 29_000),
            Row(3, swimmerId: 30, timeMs: 30_000),
        ]);

        Assert.Equal(1, For(result, 2).CombinedPlace);
        Assert.Equal(2, For(result, 3).CombinedPlace);
        Assert.Equal(3, For(result, 1).CombinedPlace);
    }

    [Fact]
    public void RepeatedDiscipline_UsesBestTime_AndMarksOnlyBestRow()
    {
        // Реальный случай из данных: один пловец плывёт дисциплину дважды за событие.
        var result = CombinedPlaceCalculator.Calculate([
            Row(1, swimmerId: 10, timeMs: 31_000),   // день 1
            Row(2, swimmerId: 10, timeMs: 29_500),   // день 2 — быстрее
            Row(3, swimmerId: 20, timeMs: 30_000),
        ]);

        // Место у пловца одно и то же на обеих строках — оно принадлежит пловцу, не заплыву.
        Assert.Equal(1, For(result, 1).CombinedPlace);
        Assert.Equal(1, For(result, 2).CombinedPlace);
        Assert.Equal(2, For(result, 3).CombinedPlace);

        // Но лучший заплыв — ровно один.
        Assert.False(For(result, 1).IsBestResult);
        Assert.True(For(result, 2).IsBestResult);
        Assert.Equal(29_500, For(result, 1).BestTimeMs);
    }

    [Fact]
    public void FailedSwim_GetsNoPlace_ButDoesNotBlockSwimmer()
    {
        // Второй заплыв пловца 10 — DSQ: место у него остаётся по зачтённому времени.
        var result = CombinedPlaceCalculator.Calculate([
            Row(1, swimmerId: 10, timeMs: 29_000),
            Row(2, swimmerId: 10, timeMs: null, timeFail: true),
            Row(3, swimmerId: 20, timeMs: 30_000),
        ]);

        Assert.Equal(1, For(result, 1).CombinedPlace);
        Assert.Null(For(result, 2).CombinedPlace);
        Assert.False(For(result, 2).IsBestResult);
        Assert.Equal(2, For(result, 3).CombinedPlace);
    }

    [Fact]
    public void SwimmerWithOnlyFailedSwims_HasNoPlaceAtAll()
    {
        var result = CombinedPlaceCalculator.Calculate([
            Row(1, swimmerId: 10, timeMs: null, timeFail: true),
            Row(2, swimmerId: 20, timeMs: 30_000),
        ]);

        Assert.Null(For(result, 1).CombinedPlace);
        Assert.Null(For(result, 1).BestTimeMs);
        Assert.Equal(1, For(result, 2).CombinedPlace);
    }

    [Fact]
    public void DisciplinesAreRankedIndependently()
    {
        var result = CombinedPlaceCalculator.Calculate([
            Row(1, swimmerId: 10, timeMs: 31_000, eventKey: Free50),
            Row(2, swimmerId: 20, timeMs: 29_000, eventKey: Free50),
            Row(3, swimmerId: 10, timeMs: 40_000, eventKey: Back50),
        ]);

        Assert.Equal(2, For(result, 1).CombinedPlace);
        // В другой дисциплине тот же пловец первый, хотя время «медленнее».
        Assert.Equal(1, For(result, 3).CombinedPlace);
    }

    [Fact]
    public void EqualTimes_ShareTheSamePlace()
    {
        var result = CombinedPlaceCalculator.Calculate([
            Row(1, swimmerId: 10, timeMs: 29_000),
            Row(2, swimmerId: 20, timeMs: 29_000),
            Row(3, swimmerId: 30, timeMs: 30_000),
        ]);

        Assert.Equal(1, For(result, 1).CombinedPlace);
        Assert.Equal(1, For(result, 2).CombinedPlace);
        // Следующий за ничьёй получает место по порядку сортировки (3-й в списке → 3).
        Assert.Equal(3, For(result, 3).CombinedPlace);
    }

    [Fact]
    public void EventKeyOf_MatchesClientBuildEventKey()
    {
        // Клиентский buildEventKey склеивает стиль|дистанцию|бассейн|пол|возрастную группу.
        Assert.Equal("1|50|50m|male|10", CombinedPlaceCalculator.EventKeyOf(1, "50", "50m", "male", "10"));
        Assert.Equal("1|50|50m|male|", CombinedPlaceCalculator.EventKeyOf(1, "50", "50m", "male", ""));
    }
}
