using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Парсинг карточки игрока loglig.com (docs/loglig-id-plan.md, шаг 2). Снапшот живой карточки
/// от 2026-07-19 лежит в Fixtures/Loglig — тесты в сеть НЕ ходят.
/// </summary>
public class LogligClientParseTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Loglig", name));

    private static string Card() => Fixture("loglig-player-304199.html");

    [Fact]
    public void ParseCard_Fixture_NameBirthYearGenderClub()
    {
        var card = LogligClient.ParseCard(Card());

        Assert.NotNull(card);
        Assert.Equal("סבינה ברנצב", card!.FullName);
        Assert.Equal(2017, card.BirthYear);
        Assert.Equal("F", card.Gender);
        Assert.Equal("הפועל דולפין נתניה", card.ClubName);
    }

    [Fact]
    public void ParseCard_Fixture_ResultRowCount()
    {
        var card = LogligClient.ParseCard(Card());

        // В фикстуре таблица #pld-pb-table (личные рекорды) содержит 9 строк — отклонение от
        // ожидания задания «≥15» зафиксировано в отчёте (см. «Отклонения»): в самой фикстуре
        // 15+ строк набирается только если считать вкладки seasonalbests/results, но задание
        // явно указывает парсить именно table#pld-pb-table.
        Assert.NotNull(card);
        Assert.Equal(9, card!.Results.Count);
    }

    [Fact]
    public void ParseCard_Fixture_KnownFreestyleRow()
    {
        var card = LogligClient.ParseCard(Card());

        var row = Assert.Single(card!.Results, r => r.EventRaw == "100 חופשי");
        Assert.Equal("25", row.PoolLength.ToString());
        Assert.Equal(25, row.PoolLength);
        Assert.Equal("01:32.68", row.TimeRaw);
        Assert.Equal(92680, row.TimeMillisecond);
        Assert.Equal(new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc), row.Date);
        Assert.Equal("אליפות חדרה הפתוחה 2026", row.CompetitionName);
        Assert.False(row.IsRelay);
        Assert.Equal("freestyle", row.StyleName);
        Assert.Equal("100", row.Distance);
    }

    [Fact]
    public void ParseCard_Fixture_RelayRow_IsRelayTrue()
    {
        var card = LogligClient.ParseCard(Card());

        var row = Assert.Single(card!.Results, r => r.EventRaw == "4X50 חופשי שליחים");
        Assert.True(row.IsRelay);
        Assert.Null(row.StyleName);
    }

    [Fact]
    public void ParseCard_Fixture_HtmlEntityInCompetitionName_Decoded()
    {
        var card = LogligClient.ParseCard(Card());

        var row = Assert.Single(card!.Results, r => r.EventRaw == "4X50 חופשי שליחים");
        Assert.Contains("אליפות ישראל \"ארנה\"", row.CompetitionName);
        Assert.DoesNotContain("&quot;", row.CompetitionName);
    }

    [Fact]
    public void ParseCard_UnknownMarkup_ReturnsNull()
    {
        Assert.Null(LogligClient.ParseCard("<html></html>"));
    }

    [Theory]
    [InlineData("00:41.97", 41970)]
    [InlineData("01:32.68", 92680)]
    [InlineData("1:02:03.45", 3723450)]
    [InlineData("мусор", null)]
    [InlineData("", null)]
    public void ParseTimeToMilliseconds_KnownAndGarbage(string raw, int? expected)
    {
        Assert.Equal(expected, LogligClient.ParseTimeToMilliseconds(raw));
    }

    [Theory]
    [InlineData("100 חופשי", "freestyle")]
    [InlineData("50 גב", "backstroke")]
    [InlineData("50 חזה", "breaststroke")]
    [InlineData("50 פרפר", "butterfly")]
    [InlineData("100 מעורב אישי", "individual_medley")]
    [InlineData("מקצוע לא ידוע", null)]
    public void MapStyleName_KnownAndUnknown(string eventRaw, string? expected)
    {
        Assert.Equal(expected, LogligClient.MapStyleName(eventRaw));
    }

    [Theory]
    [InlineData("נקבה", "F")]
    [InlineData("זכר", "M")]
    [InlineData("אחר", null)]
    [InlineData(null, null)]
    public void NormalizeGender_KnownAndUnknown(string? raw, string? expected)
    {
        Assert.Equal(expected, LogligClient.NormalizeGender(raw));
    }
}
