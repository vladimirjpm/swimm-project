using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Parsing.RecordSources;

/// <summary>Одна ссылка на PDF мастерских мировых рекордов со страницы World Aquatics.</summary>
/// <param name="Url">Абсолютный URL файла (resources.fina.org).</param>
/// <param name="Label">Подпись как на сайте: «Masters World Records - LCM (as of 01.09.2026)».</param>
/// <param name="PoolType">«50m» (LCM) / «25m» (SCM) — уже в наших терминах.</param>
/// <param name="UpdatedOn">Дата из подписи «(as of dd.MM.yyyy)», если она там есть.</param>
public sealed record WaMastersRecordsLink(string Url, string Label, string PoolType, DateOnly? UpdatedOn);

/// <summary>
/// Резолвер актуальных ссылок на «Masters World Records» со страницы
/// <see cref="WorldAquaticsSource.MastersRecordsPageUrl"/>.
///
/// ⚠ Зачем он: в адресе файла зашиты дата публикации и GUID, справочник обновляется примерно
/// раз в месяц — прибитый в конфиг URL живёт до первого обновления и потом отдаёт 404. Та же
/// история, что у isr.org.il (<see cref="IsrOrgRecordsPageResolver"/>), поэтому и решение то же:
/// автозагрузка начинается со страницы-оглавления, а не с файла.
///
/// Отдаём ТОЛЬКО личные дистанции (Individual). На странице лежат ещё эстафеты и «progression»
/// (история рекордов), но эстафетные рекорды в модель <c>Record</c> сейчас не ложатся — там
/// нет эстафетных стилей, пола mixed и полос по СУММЕ возрастов четвёрки («100-119»), — а
/// progression это вообще не справочник текущих рекордов. Показывать в админке то, чего Fetch
/// не возьмёт, — врать интерфейсом.
/// </summary>
public class WaMastersRecordsPageResolver : IRecordSourceLinksProvider
{
    // Страница — обычный серверный HTML: <a ... href="....pdf" ... title="подпись">.
    private static readonly Regex AnchorRx = new(
        """<a\b[^>]*?href\s*=\s*["']([^"']+?\.pdf)["'][^>]*>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TitleRx = new(
        """title\s*=\s*["']([^"']*)["']""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>«(as of 01.09.2026)» в подписи ссылки — когда справочник последний раз собирали.</summary>
    private static readonly Regex AsOfRx = new(
        @"as\s+of\s+(?<d>\d{2})\.(?<m>\d{2})\.(?<y>\d{4})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;

    public WaMastersRecordsPageResolver(IHttpClientFactory httpClientFactory) =>
        _httpClientFactory = httpClientFactory;

    public IReadOnlyCollection<string> Sources { get; } = [WaMastersRecordsSourceProvider.SourceKey];

    public string PageUrl => WorldAquaticsSource.MastersRecordsPageUrl;

    public async Task<IReadOnlyList<RecordSourceLinkDto>> GetLinksAsync(CancellationToken ct = default)
    {
        var links = await ResolveAsync(ct);
        return links
            .Select(l => new RecordSourceLinkDto(l.Url, l.Label, l.PoolType, IsMasters: true, l.UpdatedOn))
            .ToList();
    }

    /// <summary>Скачивает страницу-оглавление и возвращает ссылки на личные справочники.</summary>
    public async Task<IReadOnlyList<WaMastersRecordsLink>> ResolveAsync(CancellationToken ct = default)
    {
        var uri = WorldAquaticsSource.EnsureDocumentWhitelisted(PageUrl);

        var client = WorldAquaticsSource.CreateClient(_httpClientFactory, TimeSpan.FromSeconds(60));
        var response = await client.GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(ct);

        return ParseLinks(html, uri);
    }

    /// <summary>
    /// Чистая функция разбора: HTML страницы → ссылки. Отдельно от сети, чтобы тест ходил по
    /// зафиксированному куску страницы, а не в интернет.
    /// </summary>
    public static IReadOnlyList<WaMastersRecordsLink> ParseLinks(string html, Uri pageUri)
    {
        var links = new List<WaMastersRecordsLink>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in AnchorRx.Matches(html))
        {
            var href = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
            if (!Uri.TryCreate(pageUri, href, out var abs)) continue;
            if (!seen.Add(abs.AbsoluteUri)) continue;

            var fileName = Uri.UnescapeDataString(abs.AbsolutePath);
            var pool = IndividualPoolOf(fileName);
            if (pool is null) continue;

            var title = TitleRx.Match(m.Value);
            var label = title.Success
                ? WebUtility.HtmlDecode(title.Groups[1].Value).Trim()
                : Path.GetFileName(fileName);

            links.Add(new WaMastersRecordsLink(abs.AbsoluteUri, label, pool, AsOfOf(label)));
        }

        return links;
    }

    /// <summary>Ссылка на нужный бассейн; null — такой файл на странице не нашёлся.</summary>
    public static WaMastersRecordsLink? Pick(IReadOnlyList<WaMastersRecordsLink> links, string poolType) =>
        links.FirstOrDefault(l => string.Equals(l.PoolType, poolType, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// «CurrentWorldRecords-Individual-LCM…pdf» → «50m». null — файл не про личные текущие
    /// рекорды: эстафеты и «WorldRecordsProgression» отсеиваются здесь, по имени файла.
    /// </summary>
    private static string? IndividualPoolOf(string fileName)
    {
        if (!fileName.Contains("CurrentWorldRecords", StringComparison.OrdinalIgnoreCase)) return null;
        if (!fileName.Contains("Individual", StringComparison.OrdinalIgnoreCase)) return null;

        if (fileName.Contains("LCM", StringComparison.OrdinalIgnoreCase)) return "50m";
        if (fileName.Contains("SCM", StringComparison.OrdinalIgnoreCase)) return "25m";

        return null;
    }

    private static DateOnly? AsOfOf(string label)
    {
        var m = AsOfRx.Match(label);
        if (!m.Success) return null;

        return DateOnly.TryParseExact(
            $"{m.Groups["d"].Value}/{m.Groups["m"].Value}/{m.Groups["y"].Value}",
            "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }
}
