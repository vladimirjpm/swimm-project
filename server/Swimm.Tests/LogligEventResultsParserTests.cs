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
    /// Мастерская эстафета (мастерс-зима 2025, loglig 11586, событие 58141): таблица из 8
    /// колонок — нет «ניקוד אישי». Разбор ждал 9/10 и молча отдавал ноль строк, отчего ремонт
    /// полос видел «у источника 0» при 132 наших. Настоящая полоса суммы возрастов — только в
    /// подзаголовке секции («מאסטרס שליחות 100-119»), в шапке события — общая «21-99».
    /// </summary>
    /// <summary>
    /// Личное событие без клубного зачёта (16891, loglig 15896, 06.11.2026): 8 колонок — место,
    /// имя, ГОД, клуб, заплыв, дорожка, время, FINA. По одному числу колонок это мастерская
    /// эстафета, и строка съезжала на колонку: год пустой, клуб «2015», заплыв 0, время не
    /// читалось (И-62, сверка мест не нашла ни одной пары).
    /// </summary>
    [Fact]
    public void IndividualWithoutClubStanding_EightColumns_BirthYearRead()
    {
        var ev = LogligEventResultsParser.Parse(Fixture("event-85705-200im-girls-noclubpoints.html"));

        Assert.False(ev.IsRelay);
        Assert.Equal("individual_medley", ev.StyleName);
        Assert.Equal("200", ev.Distance);
        Assert.Equal("female", ev.Gender);
        Assert.NotEmpty(ev.Rows);
        Assert.All(ev.Rows, r =>
        {
            Assert.True(r.BirthYear is >= 2000 and <= 2020, $"{r.FullName}: год {r.BirthYear}");
            Assert.True(r.Heat > 0, $"{r.FullName}: заплыв {r.Heat}");
            Assert.Null(r.ClubPoints);
            Assert.Null(r.PersonalPoints);
        });

        var yarden = Assert.Single(ev.Rows, r => r.FullName.Contains("שייכט"));
        Assert.Equal(2015, yarden.BirthYear);
        Assert.Contains("דולפין", yarden.Club);
        Assert.Equal(5, yarden.Heat);
        Assert.Equal(6, yarden.Lane);
        Assert.Equal("03:13.32", yarden.Time);
        Assert.Equal(249, yarden.InternationalPoints);
        Assert.Contains("כללי", yarden.Category);
    }

    /// <summary>
    /// Время с часами «00:00:00.00» (16891, 50 вольным мальчики, 05.11.2026): у двоих с местом 1
    /// время не записали. Раньше шаблон брал «00:00.00», а «00:» уходил в статус — строка
    /// выглядела снятой. Теперь: времени нет, статуса нет, место 1 (Р90). Обычное время с той
    /// же страницы читается как раньше.
    /// </summary>
    [Fact]
    public void ZeroTimeWithHours_IsNoTimeWithoutStatus()
    {
        var ev = LogligEventResultsParser.Parse(Fixture("event-85623-50free-boys-zero-time.html"));

        var zero = ev.Rows.Single(r => r.FullName.Contains("אלחזוב"));
        Assert.Null(zero.Time);
        Assert.Null(zero.FailNote);
        Assert.Equal(1, zero.Position);

        var markus = ev.Rows.Single(r => r.FullName.Contains("יעקובוביץ"));
        Assert.Equal("00:29.62", markus.Time);
        Assert.Equal(3, markus.Position);
    }

    [Fact]
    public void MastersRelay_EightColumns_RowsAreRead()
    {
        var ev = LogligEventResultsParser.Parse(Fixture("event-58141-masters-relay-women.html"));

        Assert.True(ev.IsRelay);
        Assert.Equal("female", ev.Gender);
        Assert.Equal("21-99", ev.AgeBand);
        Assert.Equal(16, ev.Rows.Count);

        var first = ev.Rows[0];
        Assert.Equal(1, first.Position);
        Assert.Equal("מכבי כפר המכביה", first.Club);
        Assert.Equal(1, first.Heat);
        Assert.Equal(7, first.Lane);
        Assert.Equal("02:23.80", first.Time);
        Assert.Equal(360, first.InternationalPoints);
        Assert.Null(first.PersonalPoints);
        Assert.Equal(24, first.ClubPoints);
        Assert.Equal("מאסטרס שליחות 100-119", first.Category);

        // Снятая команда: статус отдельно от времени, клубных очков нет (пустая ячейка).
        var dq = Assert.Single(ev.Rows, r => r.FailNote == "DQ");
        Assert.Null(dq.Time);
        Assert.Null(dq.ClubPoints);
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
