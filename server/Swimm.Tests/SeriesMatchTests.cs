using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Application.Validation;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Узнавание выпуска серии (docs/plans/official-awards-plan.md §5.8) и текстовый ввод полос/призов
/// в /Admin/Series. Названия и доли возрастов — реальные, с прода (выпуски 2025 и 2026).
/// </summary>
public class SeriesMatchTests
{
    private const string Name2025 = "תחרות הנצחה ע\"ש גל רודובסקי ועדן נימרי";
    private const string Name2026 = "תחרות השחייה הארצית ע\"ש גל רודובסקי ועדן נימרי - 23/10/2026";

    [Theory]
    [InlineData(Name2025)]
    [InlineData(Name2026)]
    [InlineData("תחרות השחייה ע״ש גל רודובסקי ועדן נימרי 2027")] // гершаим вместо кавычек
    public void BothEditions_MatchThePhrase(string name) =>
        Assert.True(SeriesMatch.NameMatches(name, ["רודובסקי ועדן נימרי"]));

    [Fact]
    public void OtherMeet_DoesNotMatch_EmptyPhraseNeither()
    {
        Assert.False(SeriesMatch.NameMatches("אליפות הים האדום 2026", ["רודובסקי ועדן נימרי"]));
        Assert.False(SeriesMatch.NameMatches(Name2026, ["", "  "]));
        Assert.False(SeriesMatch.NameMatches(Name2026, []));
    }

    [Fact]
    public void CommonPhrase_OfBothEditions_IsTheMemorialNames()
    {
        Assert.Equal("עש גל רודובסקי ועדן נימרי", SeriesMatch.CommonPhrase([Name2025, Name2026, Name2026]));
        Assert.Null(SeriesMatch.CommonPhrase([Name2026]));
        Assert.Null(SeriesMatch.CommonPhrase(["אליפות ישראל", "גביע המדינה"]));
    }

    private static readonly SeriesMatch.PartAges Seniors = new(PartId: 1, SortOrder: 1, MinSeasonAge: 14, MaxSeasonAge: null);
    private static readonly SeriesMatch.PartAges Youth = new(PartId: 2, SortOrder: 2, MinSeasonAge: 9, MaxSeasonAge: 14);

    /// <summary>Возраста дня с заданной долей «младших» (≤ 13), остальные — 14-летние и старше.</summary>
    private static List<int> Ages(int total, int youngerThan14, int exactly14)
    {
        var ages = new List<int>();
        ages.AddRange(Enumerable.Repeat(11, youngerThan14));
        ages.AddRange(Enumerable.Repeat(14, exactly14));
        ages.AddRange(Enumerable.Repeat(25, total - youngerThan14 - exactly14));
        return ages;
    }

    [Fact]
    public void Part_ByAgesOfTheDay_RealShares()
    {
        // 1772: 605 пловцов, 14–50 лет; 12.2% — четырнадцатилетние (в обеих частях) → взрослые.
        var day1 = Ages(605, youngerThan14: 0, exactly14: 74);
        // 1773: 502 пловца, 10–14; 13.3% — четырнадцатилетние → младшие.
        var day2 = Ages(502, youngerThan14: 435, exactly14: 67);

        Assert.Equal(1, SeriesMatch.PickPart([Seniors, Youth], day1, dayNumber: 1).PartId);
        Assert.Equal(2, SeriesMatch.PickPart([Seniors, Youth], day2, dayNumber: 2).PartId);
        // Номер дня не решает: день 2 со «взрослыми» возрастами уходит во взрослую часть.
        Assert.Equal(1, SeriesMatch.PickPart([Seniors, Youth], day1, dayNumber: 2).PartId);
    }

    [Fact]
    public void Part_BelowThreshold_IsNotPicked()
    {
        var mixed = Ages(100, youngerThan14: 50, exactly14: 0);

        var pick = SeriesMatch.PickPart([Seniors, Youth], mixed, dayNumber: 1);

        Assert.Null(pick.PartId);
        Assert.Contains("80", pick.Reason);
    }

    [Fact]
    public void Part_Tie_ResolvedByDayNumber_OtherwiseNotPicked()
    {
        var all14 = Ages(10, youngerThan14: 0, exactly14: 10);   // 100% в обеих частях

        Assert.Equal(2, SeriesMatch.PickPart([Seniors, Youth], all14, dayNumber: 2).PartId);
        Assert.Null(SeriesMatch.PickPart([Seniors, Youth], all14, dayNumber: null).PartId);
    }

    [Fact]
    public void Part_SinglePart_IsAlwaysIt()
    {
        Assert.Equal(1, SeriesMatch.PickPart([Seniors], [], dayNumber: null).PartId);
        Assert.Null(SeriesMatch.PickPart([], [20], dayNumber: 1).PartId);
    }

    [Fact]
    public void Bands_ParseAndFormat_RoundTrip()
    {
        Assert.True(SeriesAwardText.TryParseBands("F 10-11; M 10–12\nF 16+; 13; M -12", out var bands, out _));
        Assert.Equal(
        [
            new SeriesAwardBandDto("female", 10, 11), new SeriesAwardBandDto("male", 10, 12),
            new SeriesAwardBandDto("female", 16, null), new SeriesAwardBandDto(null, 13, 13),
            new SeriesAwardBandDto("male", null, 12),
        ], bands);
        Assert.Equal("F 10-11; M 10-12; F 16+; 13; M -12", SeriesAwardText.FormatBands(bands));
        Assert.True(SeriesAwardText.TryParseBands("", out var none, out _));
        Assert.Empty(none);
    }

    [Theory]
    [InlineData("X 10-11")]
    [InlineData("F 12-10")]
    [InlineData("girls")]
    public void Bands_Garbage_IsRejected(string text)
    {
        Assert.False(SeriesAwardText.TryParseBands(text, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Prizes_KeepCommasInText_AndRejectDuplicates()
    {
        Assert.True(SeriesAwardText.TryParsePrizes("1: 1,000 ₪ + Cup\n2 = 500 ₪\n3: 250 ₪", out var prizes, out _));
        Assert.Equal([new(1, "1,000 ₪ + Cup"), new(2, "500 ₪"), new SeriesAwardPrizeDto(3, "250 ₪")], prizes);
        Assert.Equal("1: 1,000 ₪ + Cup\n2: 500 ₪\n3: 250 ₪", SeriesAwardText.FormatPrizes(prizes));

        Assert.False(SeriesAwardText.TryParsePrizes("1: Cup\n1: Medal", out _, out _));
        Assert.False(SeriesAwardText.TryParsePrizes("first: Cup", out _, out _));
    }
}
