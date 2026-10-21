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
    public void ParseDetails_Upcoming_HasLogligIframeBeforeStart()
    {
        // Риск №1 плана стартового протокола (§1.6) снят: iframe loglig стоит на странице
        // и ДО старта. Живая страница 20.10.2026, соревнование 22.10.2026 (И-54).
        var d = IsrOrgDiscoveryProvider.ParseDetails(Fixture("isrorg-comp-16853-upcoming.html"));
        Assert.Equal(15748, d.LogligId);
        Assert.Equal("בריכת קאנטרי בית שמש", d.Venue);
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

    // ── Программа loglig: все дни ─────────────────────────────────────────────

    /// <summary>
    /// Без <c>dateFilter=הכל</c> loglig отдаёт только сегодняшний день, если он есть в
    /// программе (10.10.2026, 16849: 1 заплыв из 4). Оба читателя программы — забор заявок
    /// и id событий для импорта — обязаны просить все дни.
    /// </summary>
    [Fact]
    public async Task Programme_RequestsAllDates_ForGridAndEventIds()
    {
        var html = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Parsing", "loglig", "disciplines-15687-alldates.html"));
        var handler = new RecordingHandler(html);
        var provider = new IsrOrgDiscoveryProvider(
            new HandlerClientFactory(handler), new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        var grid = await provider.FetchDisciplineGridAsync(15687);
        Assert.Equal(4, grid.Count);
        // У предстоящего старта ссылок на результаты нет — сам сбой тут ожидаем, важен адрес.
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.FetchEventIdsAsync(15687));

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, u =>
        {
            Assert.Equal("/LeagueTable/AthleticsDisciplines/15687", u.AbsolutePath);
            Assert.Equal("dateFilter=הכל", Uri.UnescapeDataString(u.Query.TrimStart('?')));
        });
    }

    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }

    private sealed class HandlerClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
