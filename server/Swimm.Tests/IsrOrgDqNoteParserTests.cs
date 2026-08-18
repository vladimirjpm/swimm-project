using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Регресс на класс багов "заметка дисквалификации утекла в название клуба":
/// клубы-мусор вида "הפועל דולפין נתניה DNS", "SWIM TLV 8.3 SW /" и т.п.
/// (см. docs/tasks/club-merge-plan.md, фаза A).
///
/// Три причины: (1) маркер "DNS" не распознавался вовсе; (2) в RTL-реверснутой
/// ориентации заметка идёт как "7.1 SW / DQ", и срез клуба до маркера прихватывал
/// фрагменты; (3) FullResultRx не считал строку "... DQ / SW 7.1 0" полной,
/// парсер подклеивал следующую строку результата и терял её.
/// </summary>
public class IsrOrgDqNoteParserTests
{
    // ===== Юнит-уровень: ParseResultLine на готовых (нормализованных) строках =====

    [Fact]
    public void DqWithRuleNote_NormalTokenOrder_ClubClean_NoteCanonical()
    {
        // Живой пример из EN-протокола Maccabiah.
        var r = IsrOrgResultLineParser.ParseResultLine(
            "- 2 3 ZELINGER SEAN 2017 Maccabbi Weisgal DQ / SW 7.1 0");

        Assert.Null(r.Position);
        Assert.Equal("ZELINGER", r.LastName);
        Assert.Equal("SEAN", r.FirstName);
        Assert.Equal(2017, r.BirthYear);
        Assert.Equal("Maccabbi Weisgal", r.Club);
        Assert.Null(r.Time);
        Assert.Equal("DQ / SW 7.1", r.TimeFailNote);
    }

    [Fact]
    public void DqWithRuleNote_ReversedTokenOrder_ClubClean_NoteCanonical()
    {
        // После RTL-реверса ивритской строки заметка приходит как "7.1 SW / DQ" —
        // раньше "7.1 SW /" прилипало к клубу ("... 4.4 SW /"-мусор в БД).
        var r = IsrOrgResultLineParser.ParseResultLine(
            "- 2 3 ZELINGER SEAN 2017 Hapoel Dolphin Netanya 7.1 SW / DQ 0");

        Assert.Equal("Hapoel Dolphin Netanya", r.Club);
        Assert.Null(r.Time);
        Assert.Equal("DQ / SW 7.1", r.TimeFailNote);
    }

    [Fact]
    public void DnsMarker_IsRecognized_NotGluedToClub()
    {
        // Раньше "DNS" не был маркером => попадал в название клуба.
        var r = IsrOrgResultLineParser.ParseResultLine(
            "- 1 2 COHEN DANA 2015 Hapoel Dolphin Netanya DNS 0");

        Assert.Equal("Hapoel Dolphin Netanya", r.Club);
        Assert.Null(r.Time);
        Assert.Equal("DNS", r.TimeFailNote);
    }

    [Fact]
    public void DnfMarker_WithRuleNote_ClubClean()
    {
        // Живой мусор из БД: "אקוותיקים 10.2 SW / DNF" — маркер DNF в конце цепочки.
        var r = IsrOrgResultLineParser.ParseResultLine(
            "- 3 4 LEVI ADAM 2012 Aquatics 10.2 SW / DNF 0");

        Assert.Equal("Aquatics", r.Club);
        Assert.Null(r.Time);
        Assert.Equal("DNF / SW 10.2", r.TimeFailNote);
    }

    [Fact]
    public void NsMarker_StillWorks()
    {
        var r = IsrOrgResultLineParser.ParseResultLine(
            "- 4 6 LEVI NOA 2014 SWIM TLV NS 0");

        Assert.Equal("SWIM TLV", r.Club);
        Assert.Equal("NS", r.TimeFailNote);
    }

    [Fact]
    public void DqRow_ClubOnNextLine_RecoveredFromGluedTail()
    {
        // Клуб перенесён на соседнюю строку: после склейки его токены в хвосте.
        var r = IsrOrgResultLineParser.ParseResultLine(
            "- 2 3 ZELINGER SEAN 2017 DQ / SW 7.1 0 Maccabbi Weisgal");

        Assert.Equal("Maccabbi Weisgal", r.Club);
        Assert.Equal("DQ / SW 7.1", r.TimeFailNote);
    }

    // ===== Уровень ParseLines: DQ-строка не съедает следующую (EN-ветка) =====

    private static readonly string[] EnPageWithDq =
    {
        "Maccabiah 2025 -",
        "Swimming",
        "OPEN & JUNIOR",
        "05/07/2026 - 07/07/2026",
        "Results",
        "50m Freestyle - U17 Girls",
        "05/07/2026 17:00",
        "Year Of International",
        "Rank Heat Lane Last name First name Club Result",
        "Birth Score",
        "1 3 4 Goltsov Eva 2009 Israel 00:26.89 676",
        "- 3 5 ZELINGER SEAN 2017 Maccabbi Weisgal DQ / SW 7.1 0",
        "2 3 6 SHTIFT Mika 2011 Israel 00:27.79 613",
    };

    [Fact]
    public void En_DqRowWithNote_DoesNotSwallowNextResult()
    {
        var comp = Assert.Single(
            IsrOrgCompetitionParser.ParseLines(new[] { EnPageWithDq }, "EN").ToList());

        Assert.Equal(3, comp.Results.Count);

        var dq = comp.Results.Single(r => r.LastName == "ZELINGER");
        Assert.Equal("Maccabbi Weisgal", dq.Club);
        Assert.Null(dq.Time);
        Assert.Equal("DQ / SW 7.1", dq.TimeFailNote);

        // Раньше DQ-строка не считалась полной, подклеивала следующую и теряла её.
        var next = comp.Results.Single(r => r.LastName == "SHTIFT");
        Assert.Equal("00:27.79", next.Time);
        Assert.Equal(2, (int)next.Position!);
    }

    // ===== Тот же класс бага, другая причина: искажённая ячейка времени =====

    /// <summary>
    /// Живой случай: comp 6592 «ליגה 3 הפועל ירושלים» 10.01.2025, 50 баттерфляй —
    /// протокол напечатал ячейку времени как «2/.00:28». Токен не время и не DQ/NS,
    /// поэтому уезжал в клуб: появился клуб «הפועל בית שמש 2/.00:28», а вместе с ним
    /// ВТОРОЙ пловец-двойник (ключ пловца включает клуб).
    /// Теперь числовой мусор в клуб не попадает, а становится заметкой: додумывать
    /// время за федерацию мы не имеем права, но и молчать о нём нельзя.
    /// </summary>
    [Fact]
    public void GarbledTimeCell_DoesNotLeakIntoClub_BecomesNote()
    {
        var r = IsrOrgResultLineParser.ParseResultLine(
            "1 2 3 יודין דניאל 2008 הפועל בית שמש 2/.00:28 0");

        Assert.Equal("הפועל בית שמש", r.Club);
        Assert.Null(r.Time);
        Assert.Equal("2/.00:28", r.TimeFailNote);
        Assert.Equal(1, (int)r.Position!);
    }

    /// <summary>
    /// Нога эстафеты с СОСТАВНОЙ фамилией. Раньше фамилией считался только первый
    /// токен, поэтому «בן יוסף ניתאי 2012» превращалось в «ניתאי בן» — а это, по ключу
    /// импорта (фамилия|имя|год), уже ДРУГОЙ пловец. Так рядом с настоящими детьми
    /// заводились «тени»: без единого личного результата, только с ногами эстафет.
    /// </summary>
    [Theory]
    [InlineData("בן יוסף ניתאי 2012", "בן יוסף", "ניתאי")]
    [InlineData("אבו ריא סילין 2012", "אבו ריא", "סילין")]
    [InlineData("יודין דניאל 2008", "יודין", "דניאל")]
    [InlineData("WEBER-GALE Garrett 1985", "WEBER-GALE", "Garrett")]
    public void RelaySwimmerLine_KeepsCompoundLastName(string line, string last, string first)
    {
        var s = IsrOrgResultLineParser.ParseRelaySwimmerLine(line, order: 1);
        Assert.Equal(last, s.LastName);
        Assert.Equal(first, s.FirstName);
    }

    /// <summary>Цифра ВНУТРИ слова — часть названия клуба, а не мусор: «M25»,
    /// «הפועל H2O כפר שמריהו» реально есть в справочнике.</summary>
    [Theory]
    [InlineData("1 2 3 COHEN DAN 2008 M25 00:28.10 400", "M25")]
    [InlineData("1 2 3 COHEN DAN 2008 הפועל H2O כפר שמריהו 00:28.10 400", "הפועל H2O כפר שמריהו")]
    public void DigitsInsideWord_StayInClubName(string line, string expectedClub) =>
        Assert.Equal(expectedClub, IsrOrgResultLineParser.ParseResultLine(line).Club);
}
