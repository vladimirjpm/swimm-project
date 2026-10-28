using Swimm.Domain.Entities;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Официальная награда серии по регламенту (docs/plans/official-awards-plan.md §5.3). Эталон — §3
/// плана: «רודובסקי/נימרי» 2026, деньги 23.10 (все возраста, тройка по полу) и кубки 24.10 (полосы
/// по сезонному возрасту, один победитель). Очки разбиты на три старта синтетически — в эталоне
/// известны только суммы.
/// </summary>
public class SeriesAwardScoringTests
{
    /// <summary>23.10: трём лучшим без учёта возраста, ♀ и ♂ раздельно, 1000/500/250 ₪.</summary>
    private static SeriesAward MoneyAward() => new()
    {
        Kind = SeriesAwardKinds.PointsSum, Title = "Top juniors & seniors",
        CountBest = 3, SplitByGender = true, Places = 3,
        Prizes =
        [
            new SeriesAwardPrize { Place = 1, Text = "1,000 ₪ + Cup" },
            new SeriesAwardPrize { Place = 2, Text = "500 ₪" },
            new SeriesAwardPrize { Place = 3, Text = "250 ₪" },
        ]
    };

    /// <summary>24.10: кубок победителю в каждой группе регламента (§2).</summary>
    private static SeriesAward YouthCupsAward() => new()
    {
        Kind = SeriesAwardKinds.PointsSum, Title = "Outstanding young swimmers",
        CountBest = 3, SplitByGender = true, Places = 1,
        Bands =
        [
            new SeriesAwardBand { Gender = "female", MinSeasonAge = 10, MaxSeasonAge = 11 },
            new SeriesAwardBand { Gender = "male",   MinSeasonAge = 10, MaxSeasonAge = 12 },
            new SeriesAwardBand { Gender = "female", MinSeasonAge = 12, MaxSeasonAge = 13 },
            new SeriesAwardBand { Gender = "male",   MinSeasonAge = 13, MaxSeasonAge = 14 },
        ],
        Prizes = [new SeriesAwardPrize { Place = 1, Text = "Cup" }]
    };

    /// <summary>Три старта пловца с заданной суммой: total/3 с остатком на первом.</summary>
    private static IEnumerable<SeriesAwardRow> Swims(int swimmerId, string gender, int? age, int total)
    {
        var part = total / 3;
        yield return new SeriesAwardRow(swimmerId, gender, age, total - 2 * part);
        yield return new SeriesAwardRow(swimmerId, gender, age, part);
        yield return new SeriesAwardRow(swimmerId, gender, age, part);
    }

    [Fact]
    public void Money_AllAges_TopThreePerGender_WithPrizes()
    {
        // Эталон §3 (1772): ♀ 1922/1873/1823, ♂ 2101/2022/1964; четвёртые и мастерс не попадают.
        var rows = new List<SeriesAwardRow>();
        rows.AddRange(Swims(1, "female", 16, 1922));
        rows.AddRange(Swims(2, "female", 19, 1873));
        rows.AddRange(Swims(3, "female", 15, 1823));
        rows.AddRange(Swims(4, "female", 14, 1700));
        rows.AddRange(Swims(5, "female", 48, 900));
        rows.AddRange(Swims(11, "male", 18, 2101));
        rows.AddRange(Swims(12, "male", 17, 2022));
        rows.AddRange(Swims(13, "male", 22, 1964));
        rows.AddRange(Swims(14, "male", 16, 1963));

        var groups = SeriesAwardScoring.Compute(MoneyAward(), rows);

        Assert.Equal(["male", "female"], groups.Select(g => g.Gender));
        Assert.All(groups, g => Assert.Null(g.MinSeasonAge));

        var women = groups.Single(g => g.Gender == "female").Places;
        Assert.Equal([1, 2, 3], women.Select(p => p.SwimmerId));
        Assert.Equal([1922, 1873, 1823], women.Select(p => p.Points));
        Assert.Equal(["1,000 ₪ + Cup", "500 ₪", "250 ₪"], women.Select(p => p.Prize));
        Assert.Equal([642, 640, 640], women[0].Parts);

        var men = groups.Single(g => g.Gender == "male").Places;
        Assert.Equal([2101, 2022, 1964], men.Select(p => p.Points));
        Assert.All(women.Concat(men), p => Assert.False(p.IsTie));
    }

    [Fact]
    public void YouthCups_BandsBySeasonAge_OneWinnerPerBand()
    {
        // Эталон §3 (1773): победители 907 / 837 / 1290 / 1404.
        var rows = new List<SeriesAwardRow>();
        rows.AddRange(Swims(1, "female", 10, 907));   // אליאור צ'בוטר — 2017 г.р., сезонно 10
        rows.AddRange(Swims(2, "female", 11, 694));
        rows.AddRange(Swims(3, "female", 13, 1290));  // שירה ארבל
        rows.AddRange(Swims(4, "female", 12, 1156));  // מיה גרינברג — по протоколу 1-я в 11-12, тут 2-я
        rows.AddRange(Swims(11, "male", 12, 837));    // רועי כהן אלון
        rows.AddRange(Swims(12, "male", 14, 1404));   // תומר איזבקוב
        rows.AddRange(Swims(13, "male", 13, 1172));

        var groups = SeriesAwardScoring.Compute(YouthCupsAward(), rows);

        Assert.Equal(
            [("female", 10), ("male", 10), ("female", 12), ("male", 13)],
            groups.Select(g => (g.Gender!, g.MinSeasonAge!.Value)));
        Assert.Equal([907, 837, 1290, 1404], groups.Select(g => g.Places.Single().Points));
        Assert.All(groups, g => Assert.Equal("Cup", g.Places.Single().Prize));
    }

    [Fact]
    public void Bands_SwimmerOutsideAllBands_OrWithoutBirthYear_IsNotAwarded()
    {
        var rows = new List<SeriesAwardRow>();
        rows.AddRange(Swims(1, "female", 9, 2000));    // младше полос
        rows.AddRange(Swims(2, "female", null, 2000)); // года рождения нет
        rows.AddRange(Swims(3, "female", 14, 2000));   // 14-летние девочки плывут 23.10, не 24.10
        rows.AddRange(Swims(4, "female", 11, 500));

        var girls10 = SeriesAwardScoring.Compute(YouthCupsAward(), rows)
            .Single(g => g.Gender == "female" && g.MinSeasonAge == 10);

        Assert.Equal(4, girls10.Places.Single().SwimmerId);
        Assert.Empty(SeriesAwardScoring.Compute(YouthCupsAward(), rows)
            .Single(g => g.Gender == "female" && g.MinSeasonAge == 12).Places);
    }

    [Fact]
    public void Tie_SharesPlace_AndNextPlaceIsSkipped()
    {
        // rank(): двое на 2-м месте — оба с призом 2-го места, следующий 4-й и за пределами тройки.
        var rows = new List<SeriesAwardRow>();
        rows.AddRange(Swims(1, "female", 16, 1900));
        rows.AddRange(Swims(2, "female", 16, 1800));
        rows.AddRange(Swims(3, "female", 16, 1800));
        rows.AddRange(Swims(4, "female", 16, 1700));

        var women = SeriesAwardScoring.Compute(MoneyAward(), rows).Single(g => g.Gender == "female").Places;

        Assert.Equal([(1, 1), (2, 2), (3, 2)], women.Select(p => (p.SwimmerId, p.Place)));
        Assert.Equal([false, true, true], women.Select(p => p.IsTie));
        Assert.Equal(["1,000 ₪ + Cup", "500 ₪", "500 ₪"], women.Select(p => p.Prize));
    }

    [Fact]
    public void CountBest_TakesOnlyTopStarts()
    {
        var rows = new List<SeriesAwardRow>
        {
            new(1, "male", 20, 600), new(1, "male", 20, 500), new(1, "male", 20, 400), new(1, "male", 20, 300),
        };

        var place = SeriesAwardScoring.Compute(MoneyAward(), rows).Single(g => g.Gender == "male").Places.Single();

        Assert.Equal(1500, place.Points);
        Assert.Equal([600, 500, 400], place.Parts);
    }

    [Fact]
    public void BandWithoutGender_SplitByGender_GivesTwoGroups()
    {
        var award = MoneyAward();
        award.Bands = [new SeriesAwardBand { MinSeasonAge = 16 }];
        var rows = new List<SeriesAwardRow> { new(1, "male", 20, 600), new(2, "female", 20, 500), new(3, "female", 15, 900) };

        var groups = SeriesAwardScoring.Compute(award, rows);

        Assert.Equal(["male", "female"], groups.Select(g => g.Gender));
        Assert.Equal(2, groups[1].Places.Single().SwimmerId);
        Assert.All(groups, g => Assert.Null(g.MaxSeasonAge));
    }

    [Fact]
    public void NoGenderSplit_OneGroupForAll()
    {
        var award = MoneyAward();
        award.SplitByGender = false;
        var rows = new List<SeriesAwardRow> { new(1, "male", 20, 600), new(2, "female", 20, 700) };

        var group = SeriesAwardScoring.Compute(award, rows).Single();

        Assert.Null(group.Gender);
        Assert.Equal([2, 1], group.Places.Select(p => p.SwimmerId));
    }

    [Fact]
    public void UnknownKind_IsNotComputed()
    {
        var award = MoneyAward();
        award.Kind = "event-winner";

        Assert.Empty(SeriesAwardScoring.Compute(award, [new SeriesAwardRow(1, "male", 20, 600)]));
    }
}
