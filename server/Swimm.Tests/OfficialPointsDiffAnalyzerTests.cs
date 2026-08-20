using System.Collections.Generic;
using System.Linq;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Разбор расхождения с официальными клубными очками (docs/data-integrity.md §10).
/// Агрегат «наши X, официальные Y» говорит только величину долга; здесь проверяется, что
/// долг раскладывается на ПРИЧИНЫ — иначе отметка «★ расхождение» остаётся декларацией.
/// </summary>
public class OfficialPointsDiffAnalyzerTests
{
    /// <summary>Шкала соревнования 1581 (правило 4), укороченная до нужных мест.</summary>
    private static readonly Dictionary<int, int> Scale = new()
    {
        [1] = 25, [2] = 22, [3] = 20, [4] = 18, [5] = 16, [6] = 15
    };

    private static int PointsFor(int? place) =>
        place is int p && Scale.TryGetValue(p, out var points) ? points : 0;

    private static OfficialPointsRow Row(
        long id, int? position, int heat, int? timeMs, int ours, int official,
        bool suppressed = false, string section = "final")
        => new(id, section, "50 backstroke · female 15", position, heat, timeMs, suppressed, ours, official);

    /// <summary>
    /// Главный случай 1581. Финал плывут два заплыва: третий — сильнейший (A), второй —
    /// слабейший (B). Организатор раздал очки в порядке «номер заплыва, потом время»,
    /// поэтому 25/22/20 достались заплыву 2, а призёрам — 18/16/15.
    /// </summary>
    [Fact]
    public void EveningFinal_RankedByHeatNumber_IsRecognized()
    {
        var diff = OfficialPointsDiffAnalyzer.Analyze(
        [
            Row(1, 1, 3, 30_670, ours: 25, official: 18),
            Row(2, 2, 3, 30_950, ours: 22, official: 16),
            Row(3, 3, 3, 31_410, ours: 20, official: 15),
            Row(4, 4, 2, 32_530, ours: 18, official: 25),
            Row(5, 5, 2, 32_740, ours: 16, official: 22),
            Row(6, 6, 2, 32_870, ours: 15, official: 20),
        ], PointsFor);

        var group = Assert.Single(diff.Groups);
        Assert.Equal(OfficialPointsDiffAnalyzer.HeatOrder, group.Kind);
        Assert.Equal(6, group.Rows);
        // Внутри события сумма не меняется — расходятся только клубы.
        Assert.Equal(0, group.Diff);
        Assert.Equal(diff.Ours, diff.Official);
    }

    /// <summary>Утренний прямой финал того же протокола считается по местам — расхождения нет.</summary>
    [Fact]
    public void RowsThatAgree_ProduceNoGroups()
    {
        var diff = OfficialPointsDiffAnalyzer.Analyze(
        [
            Row(1, 1, 2, 29_610, ours: 25, official: 25, section: "timed-final"),
            Row(2, 2, 1, 30_670, ours: 22, official: 22, section: "timed-final"),
        ], PointsFor);

        Assert.Empty(diff.Groups);
        Assert.Equal(0, diff.Mismatched);
    }

    [Fact]
    public void PaidPrelim_AndUnpaidSwim_AreSeparateCauses()
    {
        var diff = OfficialPointsDiffAnalyzer.Analyze(
        [
            // Мы гасим предварительный (Р34), организатор за него заплатил.
            Row(1, 1, 1, 29_810, ours: 0, official: 25, suppressed: true, section: "prelim"),
            // Обратный случай: единственный заплыв дисциплины, а очков не дали.
            Row(2, 1, 1, 135_250, ours: 25, official: 0, section: "timed-final"),
        ], PointsFor);

        var paid = Assert.Single(diff.Groups, g => g.Kind == OfficialPointsDiffAnalyzer.PaidPrelim);
        Assert.Equal(-25, paid.Diff);

        var unpaid = Assert.Single(diff.Groups, g => g.Kind == OfficialPointsDiffAnalyzer.UnpaidSwim);
        Assert.Equal(25, unpaid.Diff);
    }

    /// <summary>
    /// Погашенная строка объясняется правилом, даже если её официальные очки случайно
    /// совпали с очками за ранг по заплывам: иначе причина подменилась бы совпадением.
    /// </summary>
    [Fact]
    public void SuppressedRow_IsExplainedByTheRule_NotByHeatOrder()
    {
        var diff = OfficialPointsDiffAnalyzer.Analyze(
            [Row(1, 1, 1, 29_810, ours: 0, official: 25, suppressed: true, section: "prelim")],
            PointsFor);

        Assert.Equal(OfficialPointsDiffAnalyzer.PaidPrelim, Assert.Single(diff.Groups).Kind);
    }

    /// <summary>Равные времена в одном заплыве делят ранг, следующий его пропускает.</summary>
    [Fact]
    public void EqualTimes_ShareTheRank_AndTheNextOneSkipsIt()
    {
        var diff = OfficialPointsDiffAnalyzer.Analyze(
        [
            Row(1, 3, 1, 31_170, ours: 20, official: 20),
            Row(2, 3, 1, 31_170, ours: 20, official: 20),
            // Третья строка идёт рангом 3 (место 3 занято дважды) → 20 у нас, а официально 18:
            // источник посчитал её четвёртой. Причина не в порядке заплывов.
            Row(3, 5, 1, 31_320, ours: 16, official: 18),
        ], PointsFor);

        var group = Assert.Single(diff.Groups);
        Assert.Equal(OfficialPointsDiffAnalyzer.Unexplained, group.Kind);
    }

    [Fact]
    public void Groups_AreOrderedByHowMuchTheyCost()
    {
        var diff = OfficialPointsDiffAnalyzer.Analyze(
        [
            Row(1, 1, 1, 29_810, ours: 0, official: 25, suppressed: true, section: "prelim"),
            Row(2, 1, 1, 29_820, ours: 0, official: 22, suppressed: true, section: "prelim2"),
            Row(3, 1, 1, 135_250, ours: 25, official: 0, section: "timed-final"),
        ], PointsFor);

        Assert.Equal(
            [OfficialPointsDiffAnalyzer.PaidPrelim, OfficialPointsDiffAnalyzer.UnpaidSwim],
            diff.Groups.Select(g => g.Kind));
    }
}
