using System;
using System.IO;
using System.Linq;
using Swimm.Parsing.Parsers.Loglig;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Разбор пособытийных результатов loglig — источник, который в отличие от PDF-экспорта
/// РАЗЛИЧАЕТ раунды (И13, docs/data-integrity.md §10). Фикстуры — живые страницы чемпионата
/// «אליפות ישראל arena לנוער» (loglig 15132, наш comp 1581, 19.07.2026):
/// утреннее событие 82330 (возрастная группа) и вечернее 83593 (финал поверх возрастов).
/// </summary>
public class LogligEventResultsParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "loglig", name));

    [Fact]
    public void MorningEvent_IsTimedFinal_WithFullPointsAndHeader()
    {
        var ev = LogligEventResultsParser.Parse(Fixture("event-82330-morning-girls14.html"));

        Assert.Equal("19/07/2026", ev.Date);
        Assert.Contains("אליפות ישראל arena", ev.CompetitionName);
        Assert.Equal("freestyle", ev.StyleName);
        Assert.Equal("50", ev.Distance);
        Assert.Equal("female", ev.Gender);
        Assert.Equal("14", ev.AgeBand);
        Assert.False(ev.IsRelay);

        // Утренний зачёт возрастной группы — «גמר ישיר», один пловец = одна строка.
        Assert.Equal(39, ev.Rows.Count);
        Assert.Equal(39, ev.Rows.Select(r => r.FullName).Distinct().Count());
        Assert.All(ev.Rows, r => Assert.Equal(LogligRounds.TimedFinal, r.Round));
        Assert.All(ev.Rows, r => Assert.Equal("בנות 14", r.Category));

        // Победительница: место, состав ячеек и ОБА вида очков — по ним считается зачёт.
        var winner = ev.Rows[0];
        Assert.Equal(1, winner.Position);
        Assert.Equal("מיכל אוגינץ", winner.FullName);
        Assert.Equal(2012, winner.BirthYear);
        Assert.Equal("הפועל בית שמש", winner.Club);
        Assert.Equal(4, winner.Heat);
        Assert.Equal(4, winner.Lane);
        Assert.Equal("00:26.62", winner.Time);
        Assert.Null(winner.FailNote);
        Assert.Equal(697, winner.InternationalPoints);
        Assert.Equal(5, winner.PersonalPoints);
        Assert.Equal(25, winner.ClubPoints);
    }

    [Fact]
    public void EveningFinal_IsSeparateRound_WithItsOwnPlacesAndPoints()
    {
        var ev = LogligEventResultsParser.Parse(Fixture("event-83593-evening-final.html"));

        // Событие открытое по возрастам — «נשים 13-99», а очки платятся по секциям внутри.
        Assert.Equal("female", ev.Gender);
        Assert.Equal("13-99", ev.AgeBand);
        Assert.Equal(30, ev.Rows.Count);
        Assert.All(ev.Rows, r => Assert.Equal(LogligRounds.Final, r.Round));

        // Та же пловчиха, что выиграла утро, здесь плывёт СВОЙ финал: другое время, свой
        // первый номер в своей секции и снова полные 25 клубных очков (в сумме 25 + 25).
        var michal = Assert.Single(ev.Rows, r => r.FullName == "מיכל אוגינץ");
        Assert.Equal(1, michal.Position);
        Assert.Equal("00:26.63", michal.Time);
        Assert.Equal("בנות 14", michal.Category);
        Assert.Equal(25, michal.ClubPoints);
        Assert.Equal(5, michal.PersonalPoints);

        // Секция «גמר - כללי» (общий финал) очков НЕ даёт — платят только возрастные.
        var overall = ev.Rows.Where(r => r.Category == "כללי").ToList();
        Assert.NotEmpty(overall);
        Assert.All(overall, r => Assert.Null(r.ClubPoints));
    }

    /// <summary>
    /// Статусы и «NMin» (норматив не выполнен): время у такой строки настоящее, а у DQ/NS
    /// его нет вовсе — иначе импорт записал бы статус в поле времени.
    /// </summary>
    [Fact]
    public void Statuses_AreSeparatedFromTime()
    {
        var ev = LogligEventResultsParser.Parse(Fixture("event-82330-morning-girls14.html"));

        var belowMinimum = ev.Rows.Where(r => r.FailNote == "NMin").ToList();
        Assert.NotEmpty(belowMinimum);
        Assert.All(belowMinimum, r => Assert.NotNull(r.Time));

        var noSwim = ev.Rows.Where(r => r.Time is null).ToList();
        Assert.NotEmpty(noSwim);
        Assert.All(noSwim, r => Assert.False(string.IsNullOrWhiteSpace(r.FailNote)));
    }
}
