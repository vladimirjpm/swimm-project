using System;
using System.IO;
using System.Linq;
using Swimm.Parsing.Parsers.Loglig;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Эстафетное событие пособытийного источника loglig. Фикстура — живая страница
/// «4X50 מעורב שליחים - בנות 14-15» чемпионата 15132 (наш comp 1581, событие 82438).
///
/// Зачем отдельно от личных: PDF-экспорт печатает эстафеты ОДНОЙ сквозной дисциплиной без
/// пола и возраста (места 1…48 через все полосы), и клубный зачёт из-за этого недоплачивал
/// вдвое. Полоса есть только здесь — и только в ШАПКЕ события: подзаголовок секции у всех
/// четырёх эстафетных событий 1581 подписан «גמר ישיר - נשים 19-99», хотя плывут дети.
/// </summary>
public class LogligRelayEventParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "loglig", name));

    private static LogligEventResults RelayEvent() =>
        LogligEventResultsParser.Parse(Fixture("event-82438-relay-girls14-15.html"));

    [Fact]
    public void RelayEvent_BandComesFromHeader_NotFromSectionCaption()
    {
        var ev = RelayEvent();

        Assert.True(ev.IsRelay);
        Assert.Equal("individual_medley", ev.StyleName);
        Assert.Equal("4X50", ev.Distance);
        Assert.Equal("female", ev.Gender);
        Assert.Equal("14-15", ev.AgeBand);

        // Секция говорит «взрослые 19-99» — это и есть причина брать полосу из шапки.
        Assert.All(ev.Rows, r => Assert.Equal("נשים 19-99", r.Category));
        Assert.All(ev.Rows, r => Assert.Equal(LogligRounds.TimedFinal, r.Round));
    }

    [Fact]
    public void RelayRows_CarryTeamRosterPlacesAndOfficialPoints()
    {
        var ev = RelayEvent();

        Assert.Equal(22, ev.Rows.Count);

        var winner = ev.Rows[0];
        Assert.Equal(1, winner.Position);
        Assert.Equal("מכבי קרית ביאליק", winner.Club);
        Assert.Equal("02:01.31", winner.Time);
        // Года рождения у эстафетной строки нет — колонок девять, а не десять.
        Assert.Null(winner.BirthYear);
        // Состав команда печатает одной ячейкой через запятую (в PDF ноги идут отдельными
        // строками). Мы его не импортируем: RelayMembers уже собраны из протокола.
        Assert.Equal(4, winner.FullName.Split(',').Length);
        // Клубные очки эстафет — ДВОЙНЫЕ: 25 за место × 2 (регламент п.17 «ניקוד כפול»).
        Assert.Equal(50, winner.ClubPoints);
        Assert.Equal(5, winner.PersonalPoints);
    }

    /// <summary>
    /// Команды без времени. Источник ставит им номер по порядку (20, 20, 22), но очков не
    /// платит — поэтому место такой строки к нам не едет вовсе (см. LogligRelayBandMatcher).
    /// </summary>
    [Fact]
    public void DisqualifiedTeams_HaveStatusInsteadOfTime_AndNoPoints()
    {
        var ev = RelayEvent();

        var untimed = ev.Rows.Where(r => r.Time is null).ToList();
        Assert.Equal(3, untimed.Count);
        Assert.All(untimed, r => Assert.False(string.IsNullOrWhiteSpace(r.FailNote)));
        Assert.All(untimed, r => Assert.Null(r.ClubPoints));
        Assert.Contains(untimed, r => r.FailNote == "NS");
    }
}
