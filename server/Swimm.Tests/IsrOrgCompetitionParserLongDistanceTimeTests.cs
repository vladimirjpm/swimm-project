using System;
using System.IO;
using System.Linq;
using Swimm.Application.Mapping;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Регрессия по реальному протоколу «אליפות ישראל "ארנה" לצעירים בבריכה 3 ק"מ 2026»
/// (OrgCompId 16776, 13.02.2026): 3 км в бассейне плывут дольше часа-минус, и федерация
/// печатает время в ЧАСОВОЙ форме — <c>00:40:29.16</c>, а не <c>40:29.16</c>.
///
/// Регулярки времени ждали ровно «мм:сс.дд», поэтому токен времени не распознавался
/// вообще: он уезжал в НАЗВАНИЕ КЛУБА («בני הרצליה 00:42:41.03»), а результат ложился
/// в БД без времени. Побочный ущерб импорта — 31 клуб-дубль с временем в имени.
/// </summary>
public class IsrOrgCompetitionParserLongDistanceTimeTests
{
    private static string PdfPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "isr-3km-2026.pdf");

    [Fact]
    public void HourFormatTime_IsParsedAndNotGluedToClubName()
    {
        Assert.True(File.Exists(PdfPath), $"Фикстура протокола не найдена: {PdfPath}");

        using var fs = File.OpenRead(PdfPath);
        var comps = IsrOrgCompetitionParser.ParseCompetitions(fs, "he").ToList();

        Assert.Equal(2, comps.Count);                       // бнот 12-13 и бним 12-13
        Assert.All(comps, c => Assert.Equal("3000", c.EventStyleLen));

        var all = comps.SelectMany(c => c.Results).ToList();
        Assert.Equal(32, all.Count);

        // Ни в одном названии клуба нет цифр времени — ради этого фикс и делался.
        Assert.All(all, r => Assert.DoesNotContain(':', r.Club));

        // Время есть у всех, кроме единственной NS-строки.
        var swum = all.Where(r => string.IsNullOrEmpty(r.TimeFailNote)).ToList();
        Assert.Equal(31, swum.Count);
        Assert.All(swum, r => Assert.Matches(@"^\d{2}:\d{2}:\d{2}\.\d{2}$", r.Time!));

        var winner = comps[0].Results[0];
        Assert.Equal("00:40:29.16", winner.Time);
        Assert.Equal("SWIM TLV", winner.Club);
        Assert.Equal(1, winner.Position);
    }

    [Theory]
    [InlineData("00:40:29.16", 2429160)]  // 40 мин 29.16 с
    [InlineData("01:02:03.5", 3723500)]
    [InlineData("02:45.46", 165460)]      // прежняя форма не сломалась
    [InlineData("35.64", 35640)]
    public void SwimTime_ParsesHourForm(string time, int expectedMs) =>
        Assert.Equal(expectedMs, SwimTime.ParseToMs(time));
}
