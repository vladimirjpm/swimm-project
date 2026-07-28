using System.Collections.Generic;
using System.Linq;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Зачёт High Point Swimmer по правилу (Э2.5). Правило нужно потому, что регламенты
/// принципиально разные: возрастные соревнования дают очки за место плюс замещающий бонус за
/// возрастной рекорд, а «бугрим» — сумму очков по международной таблице (§8 плана).
/// </summary>
public class PointRulesSwimmersScoringTests
{
    /// <summary>Возрастная шкала §8.A: 1→5, 2→3, 3→2, 4→1; рекорд 13, повтор 8.</summary>
    private static PointRuleSwimmers AgeRule(bool includeRelays = false, int? countBest = null, int? minSwims = null)
        => new()
        {
            Version = "2026.01-youth", PointsSource = "placement", GroupBy = "age",
            SplitByGender = true, MaxScoringPlace = 4, DefaultPoints = 0,
            RecordPoints = 13, RecordTiePoints = 8,
            IncludeRelays = includeRelays, CountBestSwims = countBest, MinSwims = minSwims,
            Entries =
            [
                new PointRuleSwimmersEntry { Place = 1, Points = 5 },
                new PointRuleSwimmersEntry { Place = 2, Points = 3 },
                new PointRuleSwimmersEntry { Place = 3, Points = 2 },
                new PointRuleSwimmersEntry { Place = 4, Points = 1 },
            ]
        };

    /// <summary>Правило бугрим §8.B.1: FINA-очки, один кубок на пол.</summary>
    private static PointRuleSwimmers FinaRule() => new()
    {
        Version = "2026.01-adults", PointsSource = "fina", GroupBy = "none",
        SplitByGender = true, FinalsOnly = true
    };

    private static SwimmerHighPointRow Row(
        int swimmerId, int? place, int age = 13, string gender = "male",
        int fina = 0, bool isRelay = false, bool timeFail = false,
        RecordStatus record = RecordStatus.None, string ageGroup = "13-14")
        => new(swimmerId, gender, age, ageGroup, place, fina, isRelay, timeFail, record);

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 3)]
    [InlineData(3, 2)]
    [InlineData(4, 1)]
    [InlineData(5, 0)]   // за пределами шкалы → DefaultPoints
    [InlineData(null, 0)]
    public void PlaceScale_Applied(int? place, int expected)
        => Assert.Equal(expected, PointRulesSwimmersScoring.PointsFor(AgeRule(), Row(1, place)));

    [Fact]
    public void RecordReplacesPlacePoints_NotAddsToThem()
    {
        // «כולל הניקוד עבור מדליית הזהב» — 13, а не 13 + 5.
        Assert.Equal(13, PointRulesSwimmersScoring.PointsFor(
            AgeRule(), Row(1, place: 1, record: RecordStatus.Broken)));
        Assert.Equal(8, PointRulesSwimmersScoring.PointsFor(
            AgeRule(), Row(1, place: 1, record: RecordStatus.Tied)));
    }

    [Fact]
    public void RecordCountsRegardlessOfPlace()
    {
        // Заплыв бывает сводным: 13-летний с рекордом для 13 лет может финишировать вторым
        // за 14-летним — бонус всё равно начисляется.
        Assert.Equal(13, PointRulesSwimmersScoring.PointsFor(
            AgeRule(), Row(1, place: 2, record: RecordStatus.Broken)));
        Assert.Equal(13, PointRulesSwimmersScoring.PointsFor(
            AgeRule(), Row(1, place: 9, record: RecordStatus.Broken)));
    }

    [Fact]
    public void FailedTime_AndRelays_ScoreZeroByDefault()
    {
        Assert.Equal(0, PointRulesSwimmersScoring.PointsFor(AgeRule(), Row(1, 1, timeFail: true)));
        Assert.Equal(0, PointRulesSwimmersScoring.PointsFor(AgeRule(), Row(1, 1, isRelay: true)));
        // IncludeRelays решается правилом — вопрос «идут ли эстафеты в зачёт пловца» пока открыт.
        Assert.Equal(5, PointRulesSwimmersScoring.PointsFor(
            AgeRule(includeRelays: true), Row(1, 1, isRelay: true)));
    }

    [Fact]
    public void FinaRule_SumsInternationalPoints_IgnoringPlace()
    {
        var rule = FinaRule();
        Assert.Equal(842, PointRulesSwimmersScoring.PointsFor(rule, Row(1, place: 7, fina: 842)));

        // Очки суммируются по всем заплывам (как в прежнем зашитом расчёте), поэтому два
        // заплыва по 800 и 700 перевешивают один на 900.
        var winners = PointRulesSwimmersScoring.Winners(rule,
        [
            Row(1, 1, fina: 800), Row(1, 1, fina: 700),
            Row(2, 1, fina: 900),
        ]);

        var w = Assert.Single(winners);
        Assert.Equal(1, w.SwimmerId);
        Assert.Equal(1500, w.Points);
        Assert.Equal("all", w.Bucket);   // GroupBy=none — один кубок на пол
    }

    [Fact]
    public void Winners_BucketByAge_AndSplitByGender()
    {
        var winners = PointRulesSwimmersScoring.Winners(AgeRule(),
        [
            Row(1, 1, age: 13), Row(1, 1, age: 13),                       // 10
            Row(2, 2, age: 13),                                            // 3
            Row(3, 1, age: 14),                                            // 5
            Row(4, 1, age: 13, gender: "female"),                          // 5
        ]);

        Assert.Equal(3, winners.Count);
        Assert.Contains(winners, w => w.Bucket == "13" && w.Gender == "male" && w.SwimmerId == 1 && w.Points == 10);
        Assert.Contains(winners, w => w.Bucket == "14" && w.Gender == "male" && w.SwimmerId == 3);
        Assert.Contains(winners, w => w.Bucket == "13" && w.Gender == "female" && w.SwimmerId == 4);
    }

    [Fact]
    public void Tie_ReturnsAllWinnersMarked()
    {
        var winners = PointRulesSwimmersScoring.Winners(AgeRule(),
        [
            Row(1, 1), Row(2, 1),
        ]);

        Assert.Equal(2, winners.Count);
        Assert.All(winners, w => Assert.True(w.IsTie));
        Assert.All(winners, w => Assert.Equal(5, w.Points));
    }

    [Fact]
    public void CountBestSwims_TakesTopN()
    {
        // Считаем 2 лучших: 5 + 3, третий заплыв (1 очко) в зачёт не идёт.
        var winners = PointRulesSwimmersScoring.Winners(AgeRule(countBest: 2),
        [
            Row(1, 1), Row(1, 2), Row(1, 4),
        ]);

        var w = Assert.Single(winners);
        Assert.Equal(8, w.Points);
        Assert.Equal(3, w.SwimCount);
    }

    [Fact]
    public void MinSwims_ExcludesSwimmersBelowThreshold()
    {
        var winners = PointRulesSwimmersScoring.Winners(AgeRule(minSwims: 2),
        [
            Row(1, 1),                    // один заплыв — в зачёт не идёт
            Row(2, 4), Row(2, 4),         // два заплыва, 1 + 1 = 2
        ]);

        var w = Assert.Single(winners);
        Assert.Equal(2, w.SwimmerId);
        Assert.Equal(2, w.Points);
    }

    [Fact]
    public void NoRule_NoWinners()
    {
        Assert.Empty(PointRulesSwimmersScoring.Winners(null, [Row(1, 1)]));
        Assert.Equal(0, PointRulesSwimmersScoring.PointsFor(null, Row(1, 1)));
    }
}
