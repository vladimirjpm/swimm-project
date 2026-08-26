using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Parsing.RecordSources;

/// <summary>Одна ссылка на PDF-справочник рекордов со страницы «שיאי ישראל».</summary>
/// <param name="Url">Абсолютный URL файла.</param>
/// <param name="Label">Подпись ссылки как на сайте (иврит) — её показываем админу.</param>
/// <param name="PoolType">«50m» / «25m».</param>
/// <param name="IsMasters">true — мастерс, false — בוגרים ונוער (наш age-источник).</param>
/// <param name="UpdatedOn">Дата обновления из имени файла, если она там есть (dd_MM_yyyy).</param>
/// <param name="Trusted">
/// false — подпись ссылки и имя файла ПРОТИВОРЕЧАТ друг другу, и какая из них права, машине
/// не решить. Живой случай (2026-08-24): на странице федерации ссылка
/// «שיאי מאסטרס: בריכת 25 מטר» ведёт на «שיאי ישראל בריכה ארוכה 17_08_2026.pdf» — файл НЕ
/// мастерс и НЕ короткой воды. Автозагрузка такую ссылку не берёт (см. <see cref="Pick"/>),
/// но из списка она не пропадает: админ должен видеть, что сломано на сайте, а не у нас.
/// </param>
public sealed record IsrOrgRecordsLink(
    string Url, string Label, string PoolType, bool IsMasters, DateOnly? UpdatedOn, bool Trusted = true);

/// <summary>
/// Резолвер актуальных PDF-ссылок со страницы «שיאי ישראל» (data.asp?id=1013).
///
/// ⚠ Зачем он вообще: федерация зашивает дату обновления В ИМЯ ФАЙЛА
/// («שיאי ישראל בריכה ארוכה 17_08_2026.pdf»), поэтому при каждом обновлении справочника
/// URL меняется. Прибитый в конфиг адрес живёт до первого обновления и потом молча отдаёт
/// 404 — то есть «автозагрузка» обязана начинаться со страницы-оглавления, а не с файла.
///
/// Классификация — по подписи ссылки: «מאסטרס» = мастерс, «50/25 מטר» = бассейн; запасной
/// признак берётся из имени файла («ארוכה» = длинный = 50m, «קצרה» = короткий = 25m).
/// </summary>
public class IsrOrgRecordsPageResolver : IRecordSourceLinksProvider
{
    // Иврит держим ЮНИКОД-ЭСКЕЙПАМИ, как в HebrewTextHelper: литералом в этот файл уже
    // затёк невидимый U+0008 (backspace) — он встал в начало PoolRx, и регэксп не совпадал
    // НИКОГДА. Бассейн молча определялся только по имени файла, а битая ссылка на сайте
    // федерации проехала незамеченной (docs/data-integrity.md, И-15).
    private const string Metr = "\u05DE\u05D8\u05E8";            // מטר
    private const string Masters = "\u05DE\u05D0\u05E1\u05D8\u05E8\u05E1";   // מאסטרס
    private const string Arukha = "\u05D0\u05E8\u05D5\u05DB\u05D4";   // ארוכה — длинная
    private const string Ktzara = "\u05E7\u05E6\u05E8\u05D4";          // קצרה — короткая

    // <a ... href="...pdf" ...>подпись</a> — страница простая, статический HTML без SPA.
    private static readonly Regex AnchorRx = new(
        """<a\b[^>]*?href\s*=\s*["']([^"']+?\.pdf)["'][^>]*>(.*?)</a>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TagRx = new("<[^>]+>", RegexOptions.Compiled);

    // Дата обновления в имени файла: «... 17_08_2026.pdf». У мастерс-файлов её нет
    // («... - 4_25.pdf» — это месяц_год), поэтому дата опциональна.
    // «בריכת 50 מטר» / «25 מטר» — бассейн в подписи ссылки.
    private static readonly Regex PoolRx = new(
        "(50|25)\\s*" + Metr, RegexOptions.Compiled);

    private static readonly Regex FileDateRx = new(
        @"(?<d>\d{2})_(?<m>\d{2})_(?<y>\d{4})", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration? _configuration;

    public IsrOrgRecordsPageResolver(IHttpClientFactory httpClientFactory, IConfiguration? configuration = null)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    /// <summary>
    /// Страница-оглавление: из настроек (RecordsImport:IsrOrgRecordsPageUrl) или дефолт.
    /// Читается в одном месте — иначе оба провайдера завели бы по своей копии.
    /// </summary>
    public string PageUrl
    {
        get
        {
            var configured = _configuration?["RecordsImport:IsrOrgRecordsPageUrl"];
            return string.IsNullOrWhiteSpace(configured)
                ? IsrOrgRecordsSource.RecordsPageUrlDefault
                : configured;
        }
    }

    /// <summary>Ссылки для админки — тот же резолв, но в DTO прикладного слоя.</summary>
    public async Task<IReadOnlyList<RecordSourceLinkDto>> GetLinksAsync(CancellationToken ct = default)
    {
        var links = await ResolveAsync(PageUrl, ct);
        return links
            .Select(l => new RecordSourceLinkDto(l.Url, l.Label, l.PoolType, l.IsMasters, l.UpdatedOn, l.Trusted))
            .ToList();
    }

    /// <summary>Скачивает страницу-оглавление и возвращает найденные ссылки на PDF.</summary>
    public async Task<IReadOnlyList<IsrOrgRecordsLink>> ResolveAsync(
        string pageUrl, CancellationToken ct = default)
    {
        var uri = IsrOrgRecordsSource.EnsureWhitelisted(pageUrl);

        var client = IsrOrgRecordsSource.CreateClient(_httpClientFactory);
        var response = await client.GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(ct);

        return ParseLinks(html, uri);
    }

    /// <summary>
    /// Чистая функция разбора: HTML страницы → ссылки. Отдельно от сети, чтобы тест ходил
    /// по зафиксированному фрагменту страницы, а не в интернет.
    /// </summary>
    public static IReadOnlyList<IsrOrgRecordsLink> ParseLinks(string html, Uri pageUri)
    {
        var links = new List<IsrOrgRecordsLink>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in AnchorRx.Matches(html))
        {
            var href = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
            var label = WebUtility.HtmlDecode(TagRx.Replace(m.Groups[2].Value, " "))
                .Replace(' ', ' ')   // &nbsp; в подписях на странице — обычное дело
                .Trim();

            if (!Uri.TryCreate(pageUri, href, out var abs)) continue;
            if (!seen.Add(abs.AbsoluteUri)) continue;

            // Имя файла нужно расшифрованным: в href иврит приходит percent-encoded.
            var fileName = Uri.UnescapeDataString(abs.AbsolutePath);

            // Подпись — то, что федерация ОБЕЩАЕТ; имя файла — то, что она реально отдаёт.
            // Читаем врозь, чтобы поймать расхождение (см. Trusted).
            var poolByLabel = PoolOf(label);
            var poolByFile = PoolOf(fileName);
            var pool = poolByLabel ?? poolByFile;
            if (pool is null) continue;   // не справочник рекордов — просто чужой PDF на странице

            var mastersByLabel = label.Contains(Masters, StringComparison.Ordinal);
            var mastersByFile = fileName.Contains(Masters, StringComparison.Ordinal);

            // Ссылке верим, только если подпись и файл не спорят. Спор — реальность, а не
            // теория: на 2026-08-24 «שיאי מאסטרס: בריכת 25 מטר» вело на файл длинной воды и
            // НЕ мастерс. Молчаливое доверие любому из двух источников означало бы скормить
            // парсеру мастерс-рекордов справочник בוגרים ונוער.
            var trusted = (poolByLabel is null || poolByFile is null || poolByLabel == poolByFile)
                          && mastersByLabel == mastersByFile;

            links.Add(new IsrOrgRecordsLink(
                abs.AbsoluteUri, label, pool, mastersByLabel, UpdatedOnOf(fileName), trusted));
        }

        return links;
    }

    /// <summary>
    /// Выбор одной ссылки: нужный бассейн + нужная семья (masters/age). null — не нашлась.
    ///
    /// Ссылки с расхождением подписи и файла (<c>Trusted == false</c>) автозагрузке не
    /// достаются: лучше «файл не найден, грузите руками», чем молча разобрать чужой
    /// справочник и записать его в рекорды.
    /// </summary>
    public static IsrOrgRecordsLink? Pick(
        IReadOnlyList<IsrOrgRecordsLink> links, bool isMasters, string poolType) =>
        links.FirstOrDefault(l => l.Trusted
                                  && l.IsMasters == isMasters
                                  && string.Equals(l.PoolType, poolType, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// «50 מטר» / «25 מטר» в подписи; запасной вариант — по имени файла: ארוכה (длинный)
    /// = 50m, קצרה (короткий) = 25m. null — ссылка не про рекорды.
    ///
    /// Ищем именно «NN מטר», а не голое число: в имени файла живёт дата («28_12_2025»),
    /// и по подстроке «25» туда попал бы любой файл 2025 года.
    /// </summary>
    private static string? PoolOf(string haystack)
    {
        var m = PoolRx.Match(haystack);
        if (m.Success) return m.Groups[1].Value + "m";

        if (haystack.Contains(Arukha, StringComparison.Ordinal)) return "50m";
        if (haystack.Contains(Ktzara, StringComparison.Ordinal)) return "25m";

        return null;
    }

    private static DateOnly? UpdatedOnOf(string fileName)
    {
        var m = FileDateRx.Match(fileName);
        if (!m.Success) return null;

        return DateOnly.TryParseExact(
            $"{m.Groups["d"].Value}/{m.Groups["m"].Value}/{m.Groups["y"].Value}",
            "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }
}
