using System.Collections.Generic;
using System.Linq;
using Swimm.Parsing.Helpers;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;
using PW = Swimm.Parsing.Parsers.IsrOrg.IsrOrgCompetitionParser.PositionedWord;

namespace Swimm.Tests;

/// <summary>
/// И-49: ивритская нога эстафеты, перенесённая в ячейке на строки над и под строкой года.
/// PdfPig отдаёт ивритское слово ВИЗУАЛЬНО (буквы задом наперёд), а строку потом целиком
/// переворачивает <see cref="HebrewTextHelper.NormalizeHebrewLine"/>. Склейка сырых кусков
/// ставила хвост в начало («קיבוגוסלבס») и не знала пробела («גנדלריזהר») — 70 теней в базе.
/// Случаи — с протоколов loglig 12369 (compID 6652) и 11586 (6595). Проверяем то, что увидит
/// <c>ParseRelaySwimmerLine</c>: строку ПОСЛЕ переворота, «Фамилия… Имя Год».
/// </summary>
public class IsrOrgRelayHebrewWrapTests
{
    // Шапка ивритской таблицы ног (координаты — Maccabiah HE, как в
    // IsrOrgCompetitionParserHebrewRtlRoleMappingTests): «יטרפ» — имя, «החפשמ» — фамилия.
    private static List<List<PW>> HeaderRows() => new()
    {
        new() { new PW("כהס", 182.3), new PW("תנש", 471.5), new PW("מש", 548.6) },
        new() { new PW("'מ", 32.1), new PW("200", 41.8), new PW("כהס", 101.7), new PW("יטרפ", 497.8), new PW("מש", 517.3) },
        new() { new PW("'מ", 179.0), new PW("150", 188.6), new PW("דילה", 470.9), new PW("החפשמ", 540.8) },
    };

    /// <summary>Слово, как его отдаёт PdfPig: буквы в обратном порядке.</summary>
    private static string V(string logical) => new(logical.Reverse().ToArray());

    private static string ReconstructLeg(string? lastAbove, string year, string first, string? lastBelow)
    {
        var groups = HeaderRows();
        var lines = new List<string> { "h1", "h2", "h3" };
        if (lastAbove != null) { groups.Add(new() { new PW(V(lastAbove), 540.1) }); lines.Add(V(lastAbove)); }
        groups.Add(new() { new PW(year, 471.9), new PW(V(first), 506.4) });
        lines.Add($"{year} {V(first)}");
        if (lastBelow != null) { groups.Add(new() { new PW(V(lastBelow), 551.8) }); lines.Add(V(lastBelow)); }

        var result = IsrOrgCompetitionParser.ReconstructEnRelaySwimmerNames(
            groups, lines, null, null, null, out _, out _, out var isHebrewVocab);

        Assert.True(isHebrewVocab);
        Assert.Equal(new[] { "h1", "h2", "h3" }, result.Take(3));
        return HebrewTextHelper.NormalizeHebrewLine(Assert.Single(result.Skip(3)));
    }

    [Theory]
    // Хвост слова (≤ 2 буквы) — без пробела, в КОНЕЦ: было «קיבוגוסלבס», «יקובילינסק», «בבובובניקו».
    [InlineData("בוגוסלבס", "קי", "מארק", "2015", "בוגוסלבסקי מארק 2015")]
    [InlineData("קובילינסק", "י", "דניס", "1990", "קובילינסקי דניס 1990")]
    [InlineData("בובובניקו", "ב", "סרגיי", "1965", "בובובניקוב סרגיי 1965")]
    // Перенос между словами — через пробел: было «גנדלריזהר», «ביטוןבחמנד».
    [InlineData("יזהר", "גנדלר", "נועה", "2013", "יזהר גנדלר נועה 2013")]
    [InlineData("קסטן", "מגורי", "עמית", "2009", "קסטן מגורי עמית 2009")]
    // Короткий хвост после КОНЕЧНОЙ буквы — новое слово: было «בץרבינוביץ».
    [InlineData("רבינוביץ", "בץ", "טל", "1997", "רבינוביץ בץ טל 1997")]
    public void WrappedHebrewLastName_IsJoinedInLogicalOrder(
        string above, string below, string first, string year, string expected)
    {
        Assert.Equal(expected, ReconstructLeg(above, year, first, below));
    }

    [Fact]
    public void UnwrappedHebrewLeg_IsUnchanged()
    {
        var groups = HeaderRows();
        groups.Add(new() { new PW("2014", 471.9), new PW(V("ארסני"), 506.4), new PW(V("סקריפניקוב"), 546.6) });
        var lines = new List<string> { "h1", "h2", "h3", $"2014 {V("ארסני")} {V("סקריפניקוב")}" };

        var result = IsrOrgCompetitionParser.ReconstructEnRelaySwimmerNames(
            groups, lines, null, null, null, out _, out _, out _);

        Assert.Equal("סקריפניקוב ארסני 2014", HebrewTextHelper.NormalizeHebrewLine(result[3]));
    }

    // Шапка следующей страницы не нога недобранной команды (И-49, PDF loglig 11792/14575/13627).
    private static readonly string[] HeaderPage =
    {
        V("מוקדמות") + " " + V("אליפות") + " 2025 " + V("חורף"), // сырые строки — визуальный порядок
        "01/02/2025 - 24/01/2025",
        V("תוצאות"),
    };

    [Theory]
    [InlineData("09/05/2026")]
    [InlineData("- 23/12/2025")]
    public void DateLine_IsPageHeader(string line) =>
        Assert.True(IsrOrgCompetitionParser.IsPageHeaderLine(line, HeaderPage, isHE: true));

    [Fact]
    public void TitleLine_OnPageWithHeader_IsPageHeader()
    {
        var title = HebrewTextHelper.NormalizeHebrewLine(HeaderPage[0]);
        Assert.True(IsrOrgCompetitionParser.IsPageHeaderLine(title, HeaderPage, isHE: true));
    }

    [Fact]
    public void FirstLineOfContinuationPage_IsALeg_NotHeader()
    {
        // Страница-продолжение без шапки начинается прямо с ноги (14575 стр. 23).
        var page = new[] { $"2012 {V("ספיר")} {V("קאושינסקי")}", $"2013 {V("נועם")} {V("לוי")}" };
        var leg = HebrewTextHelper.NormalizeHebrewLine(page[0]);
        Assert.False(IsrOrgCompetitionParser.IsPageHeaderLine(leg, page, isHE: true));
    }

    [Theory]
    [InlineData("גוסטמלסק", "י", true)]
    [InlineData("SHOHA", "M", true)]
    [InlineData("רבינוביץ", "בץ", false)]   // конечная ץ — слово кончилось
    [InlineData("יזהר", "גנדלר", false)]
    public void IsMidWordWrap_SharedRule(string before, string part, bool expected) =>
        Assert.Equal(expected, HebrewTextHelper.IsMidWordWrap(before, part));
}
