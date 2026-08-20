using System.Linq;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сопоставление эстафетных строк пособытийного источника с нашими (docs/data-integrity.md §10).
/// Проверяем ровно то, ради чего матчер существует: полоса и место приезжают с сайта,
/// пары находятся по «клуб + время» несмотря на разное написание времени, а любая
/// неоднозначность делает план неприменимым ЦЕЛИКОМ.
/// </summary>
public class LogligRelayBandMatcherTests
{
    private static RelayRowFromSource Src(
        string club, int? timeMs, int? position, string gender = "female", string band = "14-15",
        int points = 0, string style = "individual_medley")
        => new(style, "4X50", club, timeMs, position, gender, band, points);

    private static RelayRowInDb Ours(
        long id, string club, int? timeMs, int? position, string style = "individual_medley")
        => new(id, style, "4X50", club, timeMs, position, "none", "15", "15-16", null);

    [Fact]
    public void BandAndPlace_ComeFromSource_SweepingPlacesAreReplaced()
    {
        var plan = LogligRelayBandMatcher.Build(
            [Src("מכבי חיפה", 121_310, 1, points: 50), Src("הפועל בת ים", 123_120, 2, points: 44)],
            [Ours(1, "מכבי חיפה", 121_310, 7), Ours(2, "הפועל בת ים", 123_120, 9)]);

        Assert.True(plan.CanApply);
        Assert.Equal(2, plan.Changes.Count);

        var first = plan.Changes.Single(c => c.ResultId == 1);
        Assert.Equal("none", first.GenderBefore);
        Assert.Equal("female", first.GenderAfter);
        Assert.Equal("15", first.BandBefore);
        Assert.Equal("14-15", first.BandAfter);
        Assert.Equal(7, first.PositionBefore);
        Assert.Equal(1, first.PositionAfter);
        Assert.Equal(50, first.OfficialAfter);
        Assert.True(first.HasChanges);
    }

    /// <summary>
    /// Время источника «01:57.00» против нашего «01:57.0» — на посимвольном сравнении пара
    /// терялась. Ключ считается по миллисекундам, поэтому одна и та же команда сходится.
    /// </summary>
    [Fact]
    public void TimeIsComparedInMilliseconds_NotAsText()
    {
        var ms = SwimTime.ParseToMs("01:57.00");
        Assert.Equal(SwimTime.ParseToMs("01:57.0"), ms);

        var plan = LogligRelayBandMatcher.Build(
            [Src("הפועל ירושלים", ms, 12, points: 18)],
            [Ours(1, "הפועל ירושלים", ms, 31)]);

        Assert.True(plan.CanApply);
        Assert.Equal(12, Assert.Single(plan.Changes).PositionAfter);
    }

    /// <summary>
    /// Команда без времени (DQ/NS) места не занимает: источник печатает ей номер по порядку,
    /// но очков за него не платит, и наш движок правил не должен видеть там места вовсе.
    /// </summary>
    [Fact]
    public void TeamWithoutTime_LosesItsPlace()
    {
        var plan = LogligRelayBandMatcher.Build(
            [Src("מכבי שוהם", null, 22)],
            [Ours(1, "מכבי שוהם", null, 48)]);

        Assert.True(plan.CanApply);
        var change = Assert.Single(plan.Changes);
        Assert.Null(change.PositionAfter);
        Assert.Equal(0, change.OfficialAfter);
    }

    [Fact]
    public void Rows_OfDifferentStyles_DoNotMatchEachOther()
    {
        var plan = LogligRelayBandMatcher.Build(
            [Src("מכבי חיפה", 100_000, 1), Src("מכבי חיפה", 100_000, 1, style: "freestyle")],
            [Ours(1, "מכבי חיפה", 100_000, 5), Ours(2, "מכבי חיפה", 100_000, 6, style: "freestyle")]);

        Assert.True(plan.CanApply);
        Assert.Equal(2, plan.Changes.Count);
    }

    /// <summary>Апострофы в названии клуба у сайта и в базе — разные символы; это не расхождение.</summary>
    [Fact]
    public void ClubApostrophes_AreNormalized()
    {
        var plan = LogligRelayBandMatcher.Build(
            [Src("מ'ועדון חיפה", 100_000, 1)],
            [Ours(1, "מ׳ועדון  חיפה", 100_000, 4)]);

        Assert.True(plan.CanApply);
        Assert.Single(plan.Changes);
    }

    [Fact]
    public void AmbiguousOrUnpairedRows_MakeThePlanInapplicable()
    {
        // Два клуба-тёзки с одинаковым временем — какая строка чья, неизвестно.
        var ambiguous = LogligRelayBandMatcher.Build(
            [Src("מכבי חיפה", 100_000, 1), Src("מכבי חיפה", 100_000, 2)],
            [Ours(1, "מכבי חיפה", 100_000, 5), Ours(2, "מכבי חיפה", 100_000, 6)]);
        Assert.False(ambiguous.CanApply);

        // У источника есть команда, которой нет у нас (и наоборот) — тоже стоп.
        var unpaired = LogligRelayBandMatcher.Build(
            [Src("מכבי חיפה", 100_000, 1)],
            [Ours(1, "הפועל בת ים", 100_000, 1)]);
        Assert.False(unpaired.CanApply);
        Assert.Equal(2, unpaired.Problems.Count);
    }

    /// <summary>Повторный прогон по уже починенным строкам не должен ничего писать.</summary>
    [Fact]
    public void SecondRun_FindsNothingToChange()
    {
        var plan = LogligRelayBandMatcher.Build(
            [Src("מכבי חיפה", 121_310, 1, points: 50)],
            [new RelayRowInDb(1, "individual_medley", "4X50", "מכבי חיפה", 121_310, 1,
                "female", "14-15", "14-15", 50)]);

        Assert.True(plan.CanApply);
        Assert.False(Assert.Single(plan.Changes).HasChanges);
    }
}
