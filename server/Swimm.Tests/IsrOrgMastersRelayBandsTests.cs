using System;
using System.IO;
using System.Linq;
using Swimm.Infrastructure.Services;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Мастерс-зима 2025 (isr.org.il 6595, loglig 11586): две поломки, найденные в превью затягивания.
///
/// 1. Эстафеты. Заголовок «4X50 מעורב שליחים» напечатан один раз и без пола, дальше идут
///    только строки полос «מאסטרס שליחות 100-119», «… 120-159». Парсер брал полосу лишь сразу
///    после заголовка — все 68 команд ложились одним событием. Хуже того, над первой командой
///    протокол напечатал полосу ЛИЧНОГО вида «מאסטרס נ 85-89», и вся эстафета (мужские,
///    женские, смешанные команды) становилась женской — превью сверяло мужскую команду
///    הפועל עומר 01:49.75 с женским открытым рекордом 1:50.51 и кричало «побьёт рекорд».
/// 2. Флаг Masters. Парсер знал о мастерсе только по имени файла, а у затянутого с loglig
///    имя — номер; превью к тому же искало `"is_masters":true`, а парсер пишет строку.
///
/// Фикстура — страницы 13, 14, 23, 24 настоящего протокола: начало эстафеты, её конец и
/// первый ЛИЧНЫЙ заплыв после неё (100 на спине, мужчины).
/// </summary>
public class IsrOrgMastersRelayBandsTests
{
    private static string PdfPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "MastersWinter2025_6595_relay_then_individual_HE.pdf");

    [Fact]
    public void RealPdf_EachBandLine_StartsItsOwnRelayEvent_WithoutGender()
    {
        using var fs = File.OpenRead(PdfPath);
        var comps = IsrOrgCompetitionParser.ParseCompetitions(fs, "he")
            .Where(c => c.EventStyleLen.Contains('X')).ToList();

        Assert.Equal(["85-89", "100-119", "120-159"], comps.Select(c => c.EventStyleAge));
        // Пол в шапке не напечатан — «не знаем»; mixed выведет импорт по составу (Э4).
        Assert.All(comps, c => Assert.Equal("none", c.EventStyleGender));

        // Полоса «100-119» — ровно четыре команды страницы, первой — הפועל עומר.
        var band = comps[1];
        Assert.Equal(4, band.Results.Count);
        Assert.Equal("01:49.75", band.Results[0].Time);
        Assert.Equal(4, band.Results[0].RelaySwimmers!.Count);

        // Первая команда — одна в напечатанной полосе; нога без года (ויקטור בלכסן) в состав
        // не попадает, но строка полосы следующей команды больше не проглатывается.
        var first = Assert.Single(comps[0].Results);
        Assert.Equal("01:51.82", first.Time);
    }

    [Fact]
    public void RealPdf_IndividualSwimsAfterTheRelay_StayIndividual()
    {
        // Регресс первой версии фикса: строка «מאסטרס ג 21-29» после эстафеты считалась сменой
        // полосы ЭСТАФЕТЫ, и все личные заплывы файла уезжали в событие эстафеты.
        using var fs = File.OpenRead(PdfPath);
        var comps = IsrOrgCompetitionParser.ParseCompetitions(fs, "he").ToList();

        var individual = comps.Where(c => !c.EventStyleLen.Contains('X')).ToList();
        Assert.NotEmpty(individual);
        Assert.All(individual, c =>
        {
            Assert.Equal("male", c.EventStyleGender);
            Assert.Equal("100", c.EventStyleLen);
            Assert.DoesNotContain(c.Results, r => r.IsRelay == true);
        });
        Assert.Equal("21-29", individual[0].EventStyleAge);
        Assert.DoesNotContain(comps.Where(c => c.EventStyleLen.Contains('X')).SelectMany(c => c.Results),
            r => r.IsRelay != true);
    }

    [Theory]
    [InlineData("נ", "85-89", "none")]     // полоса личного вида в эстафете — пол не берём
    [InlineData("נ", "120-159", "female")] // настоящая полоса эстафеты с полом
    [InlineData("ג", "100-119", "male")]
    public void MastersRelayBandGender_BelowHundred_IsNotARelayBand(string gender, string age, string expected) =>
        Assert.Equal(expected, IsrOrgCompetitionParser.MastersRelayBandGender(gender, age));

    [Theory]
    [InlineData("אליפות ישראל arena למאסטרס חורף 2025", true)]
    [InlineData("ליגה מאסטרס- וייסגל רחובות", true)]
    [InlineData("Maccabiah Masters 2026", true)]
    [InlineData("מוקדמות אליפות צעירים", false)]
    [InlineData(null, false)]
    public void IsMastersTitle(string? title, bool expected) =>
        Assert.Equal(expected, IsrOrgParser.IsMastersTitle(title));

    [Fact]
    public void IsMastersFile_ByTitle_WhenFileNameIsJustANumber()
    {
        using var fs = File.OpenRead(PdfPath);
        var comps = IsrOrgCompetitionParser.ParseCompetitions(fs, "he").ToList();

        Assert.True(IsrOrgParser.IsMastersFile("11586.pdf", comps));
        Assert.False(IsrOrgParser.IsMastersFile("11586.pdf", []));
        Assert.True(IsrOrgParser.IsMastersFile("winter-masters.pdf", []));
    }

    [Theory]
    [InlineData("""[{"is_masters":"true"}]""", true)]   // так пишет парсер — строкой
    [InlineData("""[{"is_masters":true}]""", true)]
    [InlineData("""[{"is_masters": "true"}]""", true)]
    [InlineData("""[{"is_masters":"false"}]""", false)]
    public void PreviewMastersFlag_ReadsStringAndBool(string json, bool expected) =>
        Assert.Equal(expected, DiscoveryPreviewService.MastersFlagRx.IsMatch(json));
}
