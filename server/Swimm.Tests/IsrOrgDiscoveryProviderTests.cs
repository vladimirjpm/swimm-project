using Swimm.Parsing.Discovery;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Парсинг HTML isr.org.il для автозабора (фаза 6). Снапшоты живого сайта от 2026-07-15
/// лежат в Fixtures/Discovery — тесты в сеть НЕ ходят. Если сайт сменит вёрстку, эти
/// тесты продолжат проходить (снапшот старый) — живой забор упадёт явной ошибкой (B4).
/// </summary>
public class IsrOrgDiscoveryProviderTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Discovery", name));

    // ── Список competitions.asp ───────────────────────────────────────────────

    [Fact]
    public void ParseList_FinishedSeason_ParsesRowsWithIdsNamesDates()
    {
        var items = IsrOrgDiscoveryProvider.ParseList(Fixture("isrorg-competitions-finished-2026.html"));

        // В снапшоте 88 строк-соревнований (у каждой 2 ссылки comp.asp — имя и иконка).
        Assert.True(items.Count > 80, $"ожидалось >80 строк, распознано {items.Count}");
        // Конкретная известная строка снапшота: ליגה מס 1 בוגרים כפר סבא, 10.10.2025
        var known = items.Single(i => i.OrgCompId == 16689);
        Assert.Equal("ליגה מס 1 בוגרים כפר סבא", known.Name);
        Assert.Equal(new DateTime(2025, 10, 10, 0, 0, 0, DateTimeKind.Utc), known.DateStart);
        Assert.Equal(known.DateStart, known.DateEnd);
        // Дубликатов compID в одном списке нет
        Assert.Equal(items.Count, items.Select(i => i.OrgCompId).Distinct().Count());
    }

    [Fact]
    public void ParseList_MultiDayRanges_ParsedAsStartEnd()
    {
        var items = IsrOrgDiscoveryProvider.ParseList(Fixture("isrorg-competitions-finished-2026.html"));
        // В снапшоте есть диапазоны («19-20.6.2026», «5-10.7.2026») — хотя бы один многодневный
        var multi = items.Where(i => i.DateEnd > i.DateStart).ToList();
        Assert.NotEmpty(multi);
        Assert.All(multi, i => Assert.True((i.DateEnd - i.DateStart).TotalDays < 30));
    }

    [Fact]
    public void ParseList_UnknownMarkup_ReturnsEmpty()
    {
        Assert.Empty(IsrOrgDiscoveryProvider.ParseList("<html><body>совсем другая вёрстка</body></html>"));
    }

    // ── Детальная comp.asp ────────────────────────────────────────────────────

    [Fact]
    public void ParseDetails_SingleDay_VenueAndLogligId()
    {
        var d = IsrOrgDiscoveryProvider.ParseDetails(Fixture("isrorg-comp-16844-singleday.html"));
        Assert.Equal("ליגה מס 4- עמק יזרעאל", d.Name);
        Assert.Equal("בריכת ויצ\"ו נהלל", d.Venue);
        Assert.Equal(15181, d.LogligId);
        Assert.Equal(1, d.DayCount);
    }

    [Fact]
    public void ParseDetails_MultiDay_FiveDayTabs()
    {
        var d = IsrOrgDiscoveryProvider.ParseDetails(Fixture("isrorg-comp-16812-multiday.html"));
        Assert.Equal(5, d.DayCount);
        Assert.Equal(14561, d.LogligId);
    }

    [Fact]
    public void ParseDetails_UnknownMarkup_ThrowsExplicit()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => IsrOrgDiscoveryProvider.ParseDetails("<html>нет заголовка</html>"));
        Assert.Contains("вёрстка", ex.Message);
    }

    // ── Разбор дат ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("10.10.2025", "2025-10-10", "2025-10-10")]
    [InlineData("5.6.2026", "2026-06-05", "2026-06-05")]
    [InlineData("19-20.6.2026", "2026-06-19", "2026-06-20")]
    [InlineData("5-10.7.2026", "2026-07-05", "2026-07-10")]
    [InlineData("28.6-2.7.2026", "2026-06-28", "2026-07-02")]
    [InlineData("30.12.2025-2.1.2026", "2025-12-30", "2026-01-02")]
    public void TryParseDateRange_KnownFormats(string raw, string expectedStart, string expectedEnd)
    {
        Assert.True(IsrOrgDiscoveryProvider.TryParseDateRange(raw, out var start, out var end));
        Assert.Equal(DateTime.Parse(expectedStart), start.Date);
        Assert.Equal(DateTime.Parse(expectedEnd), end.Date);
    }

    [Theory]
    [InlineData("")]
    [InlineData("скоро")]
    [InlineData("32.13.2026")]
    [InlineData("10.10")]
    public void TryParseDateRange_Garbage_False(string raw)
        => Assert.False(IsrOrgDiscoveryProvider.TryParseDateRange(raw, out _, out _));
}
