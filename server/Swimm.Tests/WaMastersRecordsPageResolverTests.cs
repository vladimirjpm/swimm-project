using Swimm.Parsing.Parsers.WaMastersRecords;
using Swimm.Parsing.RecordSources;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Резолвер ссылок на «Masters World Records» и перевод строк файла в оси <c>Record</c>.
/// Разбор HTML — чистая функция, поэтому тест ходит по зафиксированному куску страницы
/// worldaquatics.com/masters/records (снят 16.09.2026), а не в интернет.
/// </summary>
public class WaMastersRecordsPageResolverTests
{
    private static readonly Uri PageUri = new(WorldAquaticsSource.MastersRecordsPageUrl);

    /// <summary>Как страница выглядит на самом деле: вперемешку личные, эстафеты и прогрессия.</summary>
    private const string PageHtml = """
        <div class="article-downloads">
        <a class="article-downloads__link" href="https://resources.fina.org/fina/document/2026/09/02/ec6715d0/CurrentWorldRecords-Individual-LCM-1-.pdf" download target="_blank" rel="noopener" title="Masters World Records - LCM (as of 01.09.2026)">Masters World Records - LCM</a>
        <a class="article-downloads__link" href="https://resources.fina.org/fina/document/2026/09/02/e6eef278/WorldRecordsProgression-Individual-LCM-1-.pdf" download title="Masters World Records Progression - LCM (as of 01.09.2026)">Progression</a>
        <a class="article-downloads__link" href="https://resources.fina.org/fina/document/2026/09/02/1aab141d/CurrentWorldRecords-Relay-LCM-1-.pdf" download title="Masters World Records Relays - LCM (as of 01.09.2026)">Relays</a>
        <a class="article-downloads__link" href="https://resources.fina.org/fina/document/2026/09/02/978fda25/CurrentWorldRecords-Individual-SCM-1-.pdf" download title="Masters World Records - SCM (as of 01.09.2026)">Masters World Records - SCM</a>
        <a class="article-downloads__link" href="https://resources.fina.org/fina/document/2026/09/02/b92ece8e/CurrentWorldRecords-Relay-SCM-1-.pdf" download title="Masters World Records Relays - SCM (as of 01.09.2026)">Relays</a>
        <a class="article-downloads__link" href="https://resources.fina.org/fina/document/2024/03/26/d2d89567/Championship-Meet-Records-as-of-Doha-2024.pdf" download title="Masters Championships Records">Championships</a>
        </div>
        """;

    [Fact]
    public void ParseLinks_TakesOnlyCurrentIndividual_LcmIs50m_ScmIs25m()
    {
        var links = WaMastersRecordsPageResolver.ParseLinks(PageHtml, PageUri);

        Assert.Equal(2, links.Count);
        Assert.Equal("50m", WaMastersRecordsPageResolver.Pick(links, "50m")!.PoolType);
        Assert.Contains("CurrentWorldRecords-Individual-LCM", WaMastersRecordsPageResolver.Pick(links, "50m")!.Url);
        Assert.Contains("CurrentWorldRecords-Individual-SCM", WaMastersRecordsPageResolver.Pick(links, "25m")!.Url);
    }

    /// <summary>
    /// Эстафеты и прогрессия лежат на той же странице и называются почти так же. Взять их —
    /// значит скормить парсеру личных дистанций чужой файл, поэтому отсев проверяем явно.
    /// </summary>
    [Fact]
    public void ParseLinks_SkipsRelaysProgressionAndChampionshipRecords()
    {
        var links = WaMastersRecordsPageResolver.ParseLinks(PageHtml, PageUri);

        Assert.DoesNotContain(links, l => l.Url.Contains("Relay", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(links, l => l.Url.Contains("Progression", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(links, l => l.Url.Contains("Championship", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>«(as of 01.09.2026)» из подписи — это и есть «обновлено» в карточке админки.</summary>
    [Fact]
    public void ParseLinks_ReadsAsOfDateFromLabel()
    {
        var links = WaMastersRecordsPageResolver.ParseLinks(PageHtml, PageUri);

        Assert.All(links, l => Assert.Equal(new DateOnly(2026, 9, 1), l.UpdatedOn));
    }

    /// <summary>Страница-оглавление и файлы — разные хосты, оба должны быть в whitelist.</summary>
    [Fact]
    public void Whitelist_AllowsSiteAndDocuments_RejectsEverythingElse()
    {
        Assert.Equal(WorldAquaticsSource.SiteHost,
            WorldAquaticsSource.EnsureDocumentWhitelisted(WorldAquaticsSource.MastersRecordsPageUrl).Host);
        Assert.Equal(WorldAquaticsSource.DocumentsHost,
            WorldAquaticsSource.EnsureDocumentWhitelisted("https://resources.fina.org/x.pdf").Host);

        Assert.Throws<InvalidOperationException>(
            () => WorldAquaticsSource.EnsureDocumentWhitelisted("https://evil.example/x.pdf"));

        // Отчёты рекордов эти хосты знать не обязаны — предикаты разные не случайно.
        Assert.Throws<InvalidOperationException>(
            () => WorldAquaticsSource.EnsureWhitelisted("https://resources.fina.org/x.pdf"));
    }

    [Theory]
    [InlineData("LCM", "Women", "50", "Freestyle", "female", "50m", "freestyle", "50m")]
    [InlineData("SCM", "Men", "400", "Medley", "male", "25m", "individual_medley", "400m")]
    [InlineData("LCM", "Women", "1500", "Butterfly", "female", "50m", "butterfly", "1500m")]
    public void ToRecord_MapsAxes(
        string pool, string gender, string distance, string style,
        string expectedGender, string expectedPool, string expectedStyle, string expectedDistance)
    {
        var dto = WaMastersRecordsSourceProvider.ToRecord(new WaMastersRecordRow(
            pool, gender, distance, style, "45-49", "25.37", "USA", "CUNDIFF Missy", "24 Aug 2024"));

        Assert.NotNull(dto);
        Assert.Equal("world", dto!.RegionType);
        Assert.Equal("", dto.RegionCode);
        Assert.Equal("masters", dto.Category);
        Assert.Equal("45-49", dto.AgeKey);
        Assert.Equal(expectedGender, dto.Gender);
        Assert.Equal(expectedPool, dto.PoolType);
        Assert.Equal(expectedStyle, dto.Style);
        Assert.Equal(expectedDistance, dto.Distance);
        Assert.Equal("USA", dto.HolderCountry);
        Assert.Equal("CUNDIFF Missy", dto.HolderName);
    }

    /// <summary>Дата приводится к той же форме, в которой уже лежат даты world/open.</summary>
    [Theory]
    [InlineData("24 Aug 2024", "24/08/2024")]
    [InlineData("05 Jul 2025", "05/07/2025")]
    [InlineData("7 Jun 2026", "07/06/2026")]
    [InlineData("непонятно", "непонятно")]
    public void ToRecord_NormalizesDate(string source, string expected)
    {
        var dto = WaMastersRecordsSourceProvider.ToRecord(new WaMastersRecordRow(
            "LCM", "Men", "100", "Freestyle", "30-34", "48.72", "RUS", "GRINEV Vladislav", source));

        Assert.Equal(expected, dto!.RecordDate);
    }

    /// <summary>
    /// Эстафетная строка в личный файл попасть не должна, но если источник когда-нибудь
    /// смешает их, пол <c>Mixed</c> в оси <c>Record</c> не ложится — строку молча пропускаем,
    /// а не пишем «мужским» рекордом.
    /// </summary>
    [Fact]
    public void ToRecord_SkipsRowsOutsideAxes()
    {
        Assert.Null(WaMastersRecordsSourceProvider.ToRecord(new WaMastersRecordRow(
            "LCM", "Mixed", "4x50", "Freestyle", "100-119", "01:40.00", "USA", "TEAM", "24 Aug 2024")));

        Assert.Null(WaMastersRecordsSourceProvider.ToRecord(new WaMastersRecordRow(
            "LCM", "Women", "50", "Sidestroke", "25-29", "25.37", "USA", "X", "24 Aug 2024")));
    }
}
