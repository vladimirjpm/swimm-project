using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сверка карточки loglig с нашими результатами (docs/loglig-id-plan.md, шаг 3): пороги решения,
/// допуск по времени, эстафеты/строки без времени вне сверки.
/// </summary>
public class LogligMatchServiceTests
{
    private static readonly DateTime Day1 = new(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = new(2025, 12, 22, 0, 0, 0, DateTimeKind.Utc);

    private static LogligResultRow Row(
        string eventRaw, string? distance, string? styleName, bool isRelay,
        int timeMs, DateTime date, string competition = "Comp") =>
        new(eventRaw, distance, styleName, isRelay, 25, "00:00.00", timeMs, date, competition);

    private static LogligMatchService Service() => new();

    [Fact]
    public void Match_AutoVerify_BirthYearMatchAndTwoMatchedResults()
    {
        var card = new LogligPlayerCard("Имя", 2017, "F", "Club", new List<LogligResultRow>
        {
            Row("100 חופשי", "100", "freestyle", false, 92680, Day1),
            Row("50 חופשי", "50", "freestyle", false, 41970, Day2),
        });
        var local = new List<LocalResultKey>
        {
            new(Day1, "100", "freestyle", 92680),
            new(Day2, "50", "freestyle", 41970),
        };

        var report = Service().Match(card, 2017, "Club", local);

        Assert.Equal(LogligMatchDecision.AutoVerify, report.Decision);
        Assert.True(report.BirthYearMatch);
        Assert.Equal(2, report.MatchedResultCount);
    }

    [Fact]
    public void Match_Candidate_BirthYearMatchOneResult()
    {
        var card = new LogligPlayerCard("Имя", 2017, "F", null, new List<LogligResultRow>
        {
            Row("100 חופשי", "100", "freestyle", false, 92680, Day1),
        });
        var local = new List<LocalResultKey> { new(Day1, "100", "freestyle", 92680) };

        var report = Service().Match(card, 2017, null, local);

        Assert.Equal(LogligMatchDecision.Candidate, report.Decision);
        Assert.True(report.BirthYearMatch);
        Assert.Equal(1, report.MatchedResultCount);
    }

    [Fact]
    public void Match_Candidate_BirthYearMismatchTwoResults()
    {
        var card = new LogligPlayerCard("Имя", 2016, "F", null, new List<LogligResultRow>
        {
            Row("100 חופשי", "100", "freestyle", false, 92680, Day1),
            Row("50 חופשי", "50", "freestyle", false, 41970, Day2),
        });
        var local = new List<LocalResultKey>
        {
            new(Day1, "100", "freestyle", 92680),
            new(Day2, "50", "freestyle", 41970),
        };

        var report = Service().Match(card, 2017, null, local);

        Assert.Equal(LogligMatchDecision.Candidate, report.Decision);
        Assert.False(report.BirthYearMatch);
        Assert.Equal(2, report.MatchedResultCount);
    }

    [Fact]
    public void Match_NoMatch_NothingMatches()
    {
        var card = new LogligPlayerCard("Имя", 2016, "F", null, new List<LogligResultRow>
        {
            Row("100 חופשי", "100", "freestyle", false, 92680, Day1),
        });
        var local = new List<LocalResultKey> { new(Day2, "50", "freestyle", 41970) };

        var report = Service().Match(card, 2017, null, local);

        Assert.Equal(LogligMatchDecision.NoMatch, report.Decision);
        Assert.False(report.BirthYearMatch);
        Assert.Equal(0, report.MatchedResultCount);
    }

    [Fact]
    public void Match_RelaysAndRowsWithoutTime_ExcludedFromMatching()
    {
        var card = new LogligPlayerCard("Имя", 2017, "F", null, new List<LogligResultRow>
        {
            Row("4X50 חופשי שליחים", "4X50", null, true, 0, Day1), // эстафета
            new("100 חופשי", "100", "freestyle", false, 25, "00:00.00", null, Day2, "Comp"), // без времени
        });
        var local = new List<LocalResultKey>
        {
            new(Day1, "4X50", "freestyle", 0),
            new(Day2, "100", "freestyle", 1000),
        };

        var report = Service().Match(card, 2017, null, local);

        Assert.Equal(0, report.MatchedResultCount);
        // Год рождения совпал, совпавших заплывов 0 → Candidate (не AutoVerify/NoMatch).
        Assert.Equal(LogligMatchDecision.Candidate, report.Decision);
    }

    [Theory]
    [InlineData(92680, 92700, true)]  // Δ = 20 мс — совпадение
    [InlineData(92680, 92701, false)] // Δ = 21 мс — нет
    public void Match_TimeTolerance_20MsBoundary(int localMs, int cardMs, bool shouldMatch)
    {
        var card = new LogligPlayerCard("Имя", 2017, "F", null, new List<LogligResultRow>
        {
            Row("100 חופשי", "100", "freestyle", false, cardMs, Day1),
        });
        var local = new List<LocalResultKey> { new(Day1, "100", "freestyle", localMs) };

        var report = Service().Match(card, 2017, null, local);

        Assert.Equal(shouldMatch ? 1 : 0, report.MatchedResultCount);
    }

    [Fact]
    public void Match_ClubNameMatch_IsBonusSignalOnly()
    {
        // Клуб не совпал, год не совпал, заплывов совпало 0 → NoMatch, несмотря на клуб.
        var card = new LogligPlayerCard("Имя", 2016, "F", "Club A", new List<LogligResultRow>());
        var report = Service().Match(card, 2017, "Club B", new List<LocalResultKey>());

        Assert.False(report.ClubNameMatch);
        Assert.Equal(LogligMatchDecision.NoMatch, report.Decision);
    }
}
