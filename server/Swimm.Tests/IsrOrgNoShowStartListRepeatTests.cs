using System;
using System.IO;
using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// И-46: экспорт loglig после мест и настоящих неявок напечатал стартовый лист события ЕЩЁ РАЗ,
/// целиком и с «NS» у каждого — включая проплывших. comp 1720 («תחרות הנצחה ע"ש גל רודובסקי
/// ועדן נימרי», 25/04/2025, loglig 12369): 100 вольным юноши 15-16 и 50 вольным девушки,
/// импорт завёл 164 лишние NS-строки, /Admin/Health поднял 8 `results.exact-duplicate` и
/// 50+ `results.upsert-key-collision`. Парсер теперь не берёт неявку пловца, который уже стоит
/// в событии в том же заплыве на той же дорожке. Фикстура — страницы 21–23 настоящего протокола.
/// </summary>
public class IsrOrgNoShowStartListRepeatTests
{
    private static string PdfPath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "RodovskyNimri2025_1720_ns_startlist_HE.pdf");

    [Fact]
    public void RealPdf_StartListRepeatedAsNs_SwimmerTakenOnce()
    {
        using var fs = File.OpenRead(PdfPath);
        var comp = IsrOrgCompetitionParser.ParseCompetitions(fs, "he").First();

        Assert.Equal("100", comp.EventStyleLen);
        Assert.Equal("male", comp.EventStyleGender);

        // 59 проплывших + 3 настоящие неявки (הרשקוביץ, נחום, טוקרב) — каждый по разу.
        Assert.Equal(62, comp.Results.Count);
        Assert.Equal(59, comp.Results.Count(r => !string.IsNullOrWhiteSpace(r.Time)));
        Assert.Equal(3, comp.Results.Count(r => r.TimeFailNote == "NS"));
        Assert.Equal(62, comp.Results.Select(r => (r.LastName, r.FirstName, r.BirthYear)).Distinct().Count());

        var kastan = Assert.Single(comp.Results, r => r.LastName == "קסטן מגורי");
        Assert.Equal("00:52.10", kastan.Time);

        var log = IsrOrgCompetitionParser.GetDebugLog();
        Assert.Contains("WARN no-show repeat skipped", log);
    }

    private static IsrOrgResult Row(int heat, int lane, string? time, string? fail = null) =>
        new("ISR", null, heat, lane, "כהן", "דן", 2010, "מכבי", time, fail, 0, null, null, null, null);

    [Fact]
    public void NoShow_SameHeatAndLane_IsRepeat()
    {
        Assert.True(IsrOrgCompetitionParser.IsNoShowRepeat([Row(9, 4, "00:52.10")], Row(9, 4, null, "NS")));
        Assert.True(IsrOrgCompetitionParser.IsNoShowRepeat([Row(2, 1, null, "NS")], Row(2, 1, null, "NS")));
    }

    [Fact]
    public void NoShow_OtherHeatOrLane_IsKept()
    {
        // Склеенные сессии (MergedSessionsCheck): утренний заплыв со временем + NS в финале —
        // законные две строки, финал идёт другим заплывом/дорожкой.
        Assert.False(IsrOrgCompetitionParser.IsNoShowRepeat([Row(3, 5, "00:52.10")], Row(1, 4, null, "NS")));
        Assert.False(IsrOrgCompetitionParser.IsNoShowRepeat([Row(3, 5, "00:52.10")], Row(3, 4, null, "NS")));
    }

    [Fact]
    public void NotNoShow_OrNoHeatInfo_IsKept()
    {
        // DQ — не неявка; строка со временем — не неявка; без заплыва/дорожки не судим.
        Assert.False(IsrOrgCompetitionParser.IsNoShowRepeat([Row(9, 4, "00:52.10")], Row(9, 4, null, "DQ / SW 8.3")));
        Assert.False(IsrOrgCompetitionParser.IsNoShowRepeat([Row(9, 4, null, "NS")], Row(9, 4, "00:52.10")));
        Assert.False(IsrOrgCompetitionParser.IsNoShowRepeat([Row(0, 0, "00:52.10")], Row(0, 0, null, "NS")));
    }
}
