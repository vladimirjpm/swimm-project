using System;
using System.IO;
using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// И-63 (07.11.2026): лиги печатают время с минутами ОДНОЙ цифрой — «1:26.59», а не «01:26.59».
/// Шаблон времени ждал две цифры: строка не считалась полной (FullResultRx), парсер подклеивал к
/// ней СЛЕДУЮЩУЮ строку и терял тот результат, а «1:26.59» как мусор колонки времени уходило в
/// TimeFailNote — проплывший выглядел снятым. Сверка с loglig (`--pull-places`) нашла 125 таких
/// строк в 10 соревнованиях; у этой лиги у нас было 117 строк из 143.
/// Фикстура — весь протокол «ליגה מס 3 הפועל בית שמש» (11/12/2025, compID 16753, loglig 13551).
/// </summary>
public class IsrOrgOneDigitMinutesTests
{
    private static string PdfPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "League3BeitShemesh2025_16753_one_digit_minutes_HE.pdf");

    [Fact]
    public void RealPdf_OneDigitMinutes_AreTimes_AndNoRowIsSwallowed()
    {
        using var fs = File.OpenRead(PdfPath);
        var comps = IsrOrgCompetitionParser.ParseCompetitions(fs, "he");
        var rows = comps.SelectMany(c => c.Results).ToList();

        // Ни одно время не утекло в статус снятия.
        Assert.DoesNotContain(rows, r => r.TimeFailNote is { } n && n.Contains(':'));

        // 100 брассом, девочки: 1:26.59 — 1-я (в PDF 13 строк с временем от 1:26.59 до 2:16.60).
        var timed = rows.Where(r => r.Time != null).ToList();
        Assert.Contains(timed, r => r.Time == "01:26.59" && Equals(r.Position, 1));
        Assert.Contains(timed, r => r.Time == "01:31.89");   // раньше эту строку съедала предыдущая
        Assert.Contains(timed, r => r.Time == "02:16.60");
        Assert.All(timed, r => Assert.Matches(@"^\d{2}:\d{2}\.\d{2}$", r.Time!));
    }

    [Theory]
    [InlineData("1:26.59", "01:26.59")]
    [InlineData("01:26.59", "01:26.59")]
    [InlineData("00:40:29.16", "00:40:29.16")]
    [InlineData("35.12", "35.12")]
    public void NormalizeTime_PadsOneDigitMinutes(string input, string expected) =>
        Assert.Equal(expected, IsrOrgResultLineParser.NormalizeTime(input));

    [Fact]
    public void ResultLine_OneDigitMinutes_IsTimeNotFailNote()
    {
        var r = IsrOrgResultLineParser.ParseResultLine("1 3 3 COHEN DANA 2012 Hapoel Beit Shemesh 1:26.59 373");

        Assert.Equal("01:26.59", r.Time);
        Assert.Null(r.TimeFailNote);
        Assert.Equal("Hapoel Beit Shemesh", r.Club);
    }
}
