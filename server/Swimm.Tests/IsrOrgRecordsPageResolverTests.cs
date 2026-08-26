using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Swimm.Application.Abstractions;
using Swimm.Parsing;
using Swimm.Application.Dtos;
using Swimm.Parsing.Parsers.IsrOrgAgeRecords;
using Swimm.Parsing.Parsers.IsrOrgMastersRecords;
using Swimm.Parsing.RecordSources;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Резолвер ссылок на PDF со страницы «שיאי ישראל» (isr.org.il/data.asp?id=1013).
/// Фикстура — реальный фрагмент страницы на 2026-08-22 (иврит в href приходит
/// percent-encoded, подписи — обычным текстом).
/// </summary>
public class IsrOrgRecordsPageResolverTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly Uri PageUri = new("https://isr.org.il/data.asp?id=1013");

    private const string PageHtml = """
        <div class="content">
          <p>לטבלאות השיאים המלאות כולל שיאי גילאים ונוער:</p>
          <span style="font-size:24px"><a tabindex="128" href="/pics/%D7%A9%D7%99%D7%90%D7%99%20%D7%99%D7%A9%D7%A8%D7%90%D7%9C/%D7%A9%D7%99%D7%90%D7%99%20%D7%99%D7%A9%D7%A8%D7%90%D7%9C%20%D7%91%D7%A8%D7%99%D7%9B%D7%94%20%D7%90%D7%A8%D7%95%D7%9B%D7%94%2017_08_2026.pdf">שיאי ישראל בוגרים ונוער: בריכת 50 מטר</a></span> (עדכון: 17/08/2026)
          <strong><a tabindex="128" href="/pics/%D7%A9%D7%99%D7%90%D7%99%20%D7%99%D7%A9%D7%A8%D7%90%D7%9C%20%D7%91%D7%A8%D7%99%D7%9B%D7%94%20%D7%A7%D7%A6%D7%A8%D7%94%2028_12_2025.pdf">שיאי ישראל בוגרים ונוער: בריכת 25 מטר</a></strong> (עדכון: 28/12/2025)
          <strong><a tabindex="128" href="/pics/%D7%A9%D7%99%D7%90%D7%99%D7%9D%20%D7%9E%D7%90%D7%A1%D7%98%D7%A8%D7%A1%20%D7%90%D7%A8%D7%95%D7%9B%D7%94%20-%204_25.pdf" target="_blank">שיאי מאסטרס: בריכת 50 מטר</a></strong>
          <strong><a tabindex="128" href="/pics/%D7%A9%D7%99%D7%90%D7%99%D7%9D%20%D7%9E%D7%90%D7%A1%D7%98%D7%A8%D7%A1%20%D7%A7%D7%A6%D7%A8%D7%94%20-%201_26.pdf">שיאי מאסטרס: בריכת 25 מטר</a></strong>
          <a href="/documents.asp">תקנונים וטפסים</a>
          <a href="/pics/protocol-2026.pdf">פרוטוקול אסיפה</a>
        </div>
        """;

    [Fact]
    public void ParseLinks_FindsAllFourRecordFiles()
    {
        var links = IsrOrgRecordsPageResolver.ParseLinks(PageHtml, PageUri);

        // Посторонние PDF со страницы (протокол собрания) не должны считаться справочником.
        Assert.Equal(4, links.Count);
        Assert.All(links, l => Assert.StartsWith("https://isr.org.il/pics/", l.Url));
    }

    [Theory]
    [InlineData(false, "50m", "17_08_2026")]
    [InlineData(false, "25m", "28_12_2025")]
    [InlineData(true, "50m", "4_25")]
    [InlineData(true, "25m", "1_26")]
    public void Pick_ResolvesEachFamilyAndPool(bool isMasters, string pool, string fileMarker)
    {
        var links = IsrOrgRecordsPageResolver.ParseLinks(PageHtml, PageUri);

        var link = IsrOrgRecordsPageResolver.Pick(links, isMasters, pool);

        Assert.NotNull(link);
        Assert.Equal(pool, link!.PoolType);
        Assert.Equal(isMasters, link.IsMasters);
        Assert.Contains(fileMarker, Uri.UnescapeDataString(link.Url));
    }

    [Fact]
    public void UpdatedOn_ReadFromFileName_NullWhenAbsent()
    {
        var links = IsrOrgRecordsPageResolver.ParseLinks(PageHtml, PageUri);

        // Дата обновления зашита в имя файла возрастных справочников — по ней видно,
        // что федерация меняет URL при каждом обновлении (ради чего резолвер и нужен).
        Assert.Equal(new DateOnly(2026, 8, 17),
            IsrOrgRecordsPageResolver.Pick(links, false, "50m")!.UpdatedOn);
        Assert.Equal(new DateOnly(2025, 12, 28),
            IsrOrgRecordsPageResolver.Pick(links, false, "25m")!.UpdatedOn);

        // У мастерс-файлов в имени только «месяц_год» — даты нет, и это законно.
        Assert.Null(IsrOrgRecordsPageResolver.Pick(links, true, "50m")!.UpdatedOn);
    }

    [Fact]
    public void PoolDetection_IgnoresYearDigitsInFileName()
    {
        // «28_12_2025» содержит «25», но это дата, а не бассейн: бассейн берётся из «NN מטר».
        var links = IsrOrgRecordsPageResolver.ParseLinks(PageHtml, PageUri);
        var short50 = IsrOrgRecordsPageResolver.Pick(links, false, "50m")!;

        Assert.Contains("17_08_2026", Uri.UnescapeDataString(short50.Url));
        Assert.Equal("50m", short50.PoolType);
    }

    /// <summary>
    /// Бассейн берётся ИЗ ПОДПИСИ, а не из имени файла. Проверка не теоретическая: в регэксп
    /// PoolRx литералом затёк невидимый U+0008, он не совпадал никогда, и бассейн молча
    /// определялся только по имени файла. Пока подпись и файл согласны, ошибку не видно —
    /// поэтому тест кормит ссылку, где они РАСХОДЯТСЯ (docs/data-integrity.md, И-15).
    /// </summary>
    [Fact]
    public void PoolComesFromTheLabel_NotOnlyFromTheFileName()
    {
        const string html = """
            <a href="/pics/%D7%A9%D7%99%D7%90%D7%99%20%D7%99%D7%A9%D7%A8%D7%90%D7%9C%20%D7%91%D7%A8%D7%99%D7%9B%D7%94%20%D7%90%D7%A8%D7%95%D7%9B%D7%94%2017_08_2026.pdf">שיאי ישראל בוגרים ונוער: בריכת 25 מטר</a>
            """;

        var link = Assert.Single(IsrOrgRecordsPageResolver.ParseLinks(html, PageUri));

        Assert.Equal("25m", link.PoolType);   // подпись говорит 25 м, имя файла — «ארוכה» (длинная)
        Assert.False(link.Trusted);            // …и раз они спорят, ссылке не верим
    }

    /// <summary>
    /// Живая поломка на стороне федерации (2026-08-24): ссылка «שיאי מאסטרס: בריכת 25 מטר»
    /// ведёт на «שיאי ישראל בריכה ארוכה» — файл НЕ мастерс и НЕ короткой воды. Автозагрузка
    /// обязана отказаться: скормить парсеру мастерс-рекордов справочник בוגרים ונוער хуже,
    /// чем честно сказать «не нашлось, грузите руками».
    /// </summary>
    [Fact]
    public void BrokenSiteLink_IsNotPickedForAutoFetch()
    {
        const string html = """
            <a href="/pics/%D7%A9%D7%99%D7%90%D7%99%D7%9D%20%D7%9E%D7%90%D7%A1%D7%98%D7%A8%D7%A1%20%D7%90%D7%A8%D7%95%D7%9B%D7%94%20-%204_25.pdf">שיאי מאסטרס: בריכת 50 מטר</a>
            <a href="/pics/%D7%A9%D7%99%D7%90%D7%99%20%D7%99%D7%A9%D7%A8%D7%90%D7%9C%20%D7%91%D7%A8%D7%99%D7%9B%D7%94%20%D7%90%D7%A8%D7%95%D7%9B%D7%94%2017_08_2026.pdf">שיאי מאסטרס: בריכת 25 מטר</a>
            """;

        var links = IsrOrgRecordsPageResolver.ParseLinks(html, PageUri);

        // Целая ссылка мастерс-50 берётся как обычно…
        Assert.NotNull(IsrOrgRecordsPageResolver.Pick(links, isMasters: true, "50m"));
        // …а битая мастерс-25 автозагрузке не достаётся.
        Assert.Null(IsrOrgRecordsPageResolver.Pick(links, isMasters: true, "25m"));

        // Но из СПИСКА не исчезает: админ должен видеть, что сломано у федерации, а не у нас.
        Assert.Contains(links, l => !l.Trusted && l.PoolType == "25m" && l.IsMasters);
    }

    [Fact]
    public void GoodPage_HasNoBrokenLinks()
    {
        // Контроль на нормальной фикстуре: доверие не должно теряться на ровном месте.
        Assert.All(IsrOrgRecordsPageResolver.ParseLinks(PageHtml, PageUri), l => Assert.True(l.Trusted));
    }

    [Fact]
    public void EnsureWhitelisted_RejectsForeignHost()
    {
        Assert.Throws<InvalidOperationException>(
            () => IsrOrgRecordsSource.EnsureWhitelisted("https://evil.example.com/records.pdf"));

        // Поддомены федерации разрешены, сам домен — тоже.
        Assert.Equal("isr.org.il", IsrOrgRecordsSource.EnsureWhitelisted(
            IsrOrgRecordsSource.RecordsPageUrlDefault).Host);
    }

    /// <summary>
    /// Живая проверка: страница федерации на месте и по-прежнему отдаёт все четыре файла.
    /// В обычном прогоне пропускается (сеть), включается переменной SWIMM_NET_TESTS=1.
    /// </summary>
    [Fact]
    public async Task Live_RecordsPage_StillListsFourFiles()
    {
        if (Environment.GetEnvironmentVariable("SWIMM_NET_TESTS") != "1") return;

        var resolver = new IsrOrgRecordsPageResolver(new SingleClientFactory());
        var links = await resolver.ResolveAsync(IsrOrgRecordsSource.RecordsPageUrlDefault);

        Assert.NotNull(IsrOrgRecordsPageResolver.Pick(links, false, "25m"));
        Assert.NotNull(IsrOrgRecordsPageResolver.Pick(links, false, "50m"));
        Assert.NotNull(IsrOrgRecordsPageResolver.Pick(links, true, "25m"));
        Assert.NotNull(IsrOrgRecordsPageResolver.Pick(links, true, "50m"));
    }

    /// <summary>
    /// Живая проверка всей цепочки автозагрузки: страница → ссылки → скачивание PDF →
    /// парсер → строки рекордов. Включается той же SWIMM_NET_TESTS=1.
    /// </summary>
    [Fact]
    public async Task Live_AgeAndMasters_FetchEndToEnd()
    {
        if (Environment.GetEnvironmentVariable("SWIMM_NET_TESTS") != "1") return;

        // Пустая конфигурация = «URL руками не заданы» — ровно тот путь, которым пойдёт
        // кнопка Fetch в /Admin/Import после этой задачи.
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var factory = new SingleClientFactory();
        var resolver = new IsrOrgRecordsPageResolver(factory);

        var age = new IsrOrgAgeRecordsSourceProvider(
            factory, config, new IsrOrgAgeRecordsParser(), resolver);
        var ageRows = await age.FetchAsync(new RecordSourceRequest("isrorg-age", null, null, null, null, null));

        var masters = new IsrOrgMastersRecordsSourceProvider(
            factory, config, new IsrOrgMastersRecordsParser(), resolver);
        var mastersRows = await masters.FetchAsync(
            new RecordSourceRequest("isrorg-masters", null, null, null, null, null));

        output.WriteLine($"age: {ageRows.Count} строк, masters: {mastersRows.Count} строк");
        output.WriteLine("age по категориям: " + string.Join(", ",
            ageRows.GroupBy(r => r.Category).Select(g => $"{g.Key}={g.Count()}")));

        Assert.NotEmpty(ageRows);
        Assert.NotEmpty(mastersRows);
        Assert.Contains(ageRows, r => r.PoolType == "25m");
        Assert.Contains(ageRows, r => r.PoolType == "50m");
        Assert.All(ageRows, r => Assert.Equal("ISR", r.RegionCode));
        Assert.All(mastersRows, r => Assert.Equal("masters", r.Category));
    }

    /// <summary>
    /// DI-граф: карточки Age/Masters в /Admin/Import зовут провайдера через шов
    /// IRecordSourceLinksProvider, и оба источника рекордов ждут резолвер в конструкторе.
    /// Тест ловит забытую регистрацию раньше, чем это сделает 500 в админке.
    /// </summary>
    [Fact]
    public void Di_ResolvesLinksProviderAndBothRecordSources()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddHttpClient();
        services.AddParsing();

        using var sp = services.BuildServiceProvider(validateScopes: true);

        Assert.NotNull(sp.GetRequiredService<IRecordSourceLinksProvider>());
        Assert.Equal(IsrOrgRecordsSource.RecordsPageUrlDefault,
            sp.GetRequiredService<IRecordSourceLinksProvider>().PageUrl);

        var sources = sp.GetServices<IRecordSourceProvider>().Select(p => p.Source).ToList();
        Assert.Contains("isrorg-age", sources);
        Assert.Contains("isrorg-masters", sources);
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
