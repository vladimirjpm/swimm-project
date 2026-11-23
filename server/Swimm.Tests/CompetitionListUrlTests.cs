using System;
using System.Collections.Generic;
using System.Linq;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Адрес /Admin/Competitions (хэндофф «вариант E», раздел 6): когда открывать текущий месяц,
/// сезон из даты, «season=all», ключ выбранной строки <c>sel=</c>.
/// </summary>
public class CompetitionListUrlTests
{
    private static IEnumerable<KeyValuePair<string, string?>> Q(params (string K, string? V)[] kv) =>
        kv.Select(x => new KeyValuePair<string, string?>(x.K, x.V));

    [Fact]
    public void EmptyAddress_OpensOnCurrentMonth()
    {
        Assert.True(CompetitionListUrl.ShouldOpenOnCurrentMonth(Q()));
        // Пустые поля формы — то же, что их нет.
        Assert.True(CompetitionListUrl.ShouldOpenOnCurrentMonth(Q(("q", ""), ("stage", " "))));
    }

    [Theory]
    [InlineData("q", "ליגה")]          // Jobs: «Требует внимания»
    [InlineData("q", "1645")]          // Health: № соревнования
    [InlineData("filter", "autopull-blocked")]
    [InlineData("filter", "discovery-new")]
    [InlineData("stage", "OnSite")]
    [InlineData("cat", "results-main")] // Categories
    [InlineData("kind", "champ")]       // дашборд «Дубль клубного зачёта»
    [InlineData("season", "2027")]      // сезон без месяца — весь сезон
    [InlineData("season", "all")]       // чип «Все»
    [InlineData("discipline", "all")]
    [InlineData("p", "2")]
    [InlineData("state", "waiting")]
    [InlineData("sel", "c12")]
    public void AnyParameter_KeepsTheExplicitSelection(string key, string value) =>
        Assert.False(CompetitionListUrl.ShouldOpenOnCurrentMonth(Q((key, value))));

    [Theory]
    [InlineData(2026, 11, 23, 2027, 11)]
    [InlineData(2026, 9, 1, 2027, 9)]    // сентябрь — первый месяц нового сезона
    [InlineData(2026, 8, 31, 2026, 8)]   // август — последний месяц прошлого
    [InlineData(2027, 1, 15, 2027, 1)]
    public void CurrentSeasonMonth_FederationNumbering(int y, int m, int d, int season, int month) =>
        Assert.Equal((season, month), CompetitionListUrl.CurrentSeasonMonth(new DateOnly(y, m, d)));

    [Theory]
    [InlineData("2027", 2027)]
    [InlineData("all", null)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    [InlineData("-5", null)]
    public void ParseSeason(string value, int? expected) =>
        Assert.Equal(expected, CompetitionListUrl.ParseSeason(value));

    [Fact]
    public void RowKey_OfEachKind_AndSiteKeyFindsThePulledRow()
    {
        var site = new UnifiedCompetitionRowDto
        {
            Stage = CompetitionStage.OnSite, Site = new UnifiedSiteInfo { DiscoveredId = 45 },
        };
        var single = new UnifiedCompetitionRowDto
        {
            Stage = CompetitionStage.Imported,
            Db = new CompetitionRowDto { Single = new CompetitionListItemDto { Id = 123 } },
            Site = new UnifiedSiteInfo { DiscoveredId = 45 },
        };
        var ev = new UnifiedCompetitionRowDto
        {
            Stage = CompetitionStage.DbOnly, Db = new CompetitionRowDto { EventId = 7 },
        };

        Assert.Equal("s45", site.Key);
        Assert.Equal("c123", single.Key);
        Assert.Equal("e7", ev.Key);

        // После «Затянуть» строка s45 стала c123 — адрес с sel=s45 открывает её же.
        Assert.True(CompetitionRowKey.Matches(single, "s45"));
        Assert.True(CompetitionRowKey.Matches(single, "C123"));
        Assert.False(CompetitionRowKey.Matches(ev, "c7"));
        Assert.False(CompetitionRowKey.Matches(site, null));
    }

    [Theory]
    [InlineData("c12", true)]
    [InlineData("e3", true)]
    [InlineData("s45", true)]
    [InlineData("x1", false)]
    [InlineData("c", false)]
    [InlineData("c0", false)]
    [InlineData("c-1", false)]
    [InlineData("<script>", false)]
    public void RowKey_IsValid(string key, bool ok) => Assert.Equal(ok, CompetitionRowKey.IsValid(key));
}
