namespace Swimm.Parsing.RecordSources;

/// <summary>
/// Общее для всех источников api.worldaquatics.com (отчёты рекордов, список стран):
/// whitelist домена, сборка URL и HTTP-клиент. Проверка домена — предикат безопасности
/// (SSRF), поэтому живёт ровно в одном месте: копия предиката = будущий инцидент
/// (docs/data-integrity.md §7, п.1). Сделано по образцу <see cref="IsrOrgRecordsSource"/>.
/// </summary>
public static class WorldAquaticsSource
{
    public const string ApiHost = "api.worldaquatics.com";

    /// <summary>Сайт World Aquatics: на нём живёт страница-оглавление мастерских рекордов.</summary>
    public const string SiteHost = "www.worldaquatics.com";

    /// <summary>Файловое хранилище World Aquatics: сами PDF мастерских рекордов лежат там.</summary>
    public const string DocumentsHost = "resources.fina.org";

    /// <summary>
    /// Страница-оглавление мастерских рекордов. Прямые URL файлов сюда прибивать нельзя:
    /// в адресе зашиты дата публикации и GUID
    /// (<c>/fina/document/2026/09/02/&lt;guid&gt;/CurrentWorldRecords-Individual-LCM-1-.pdf</c>),
    /// и при каждом обновлении справочника они меняются — ровно как у isr.org.il.
    /// </summary>
    public const string MastersRecordsPageUrl = "https://" + SiteHost + "/masters/records";

    /// <summary>
    /// GUID Израиля в API worldaquatics — домашний регион проекта и дефолт для NR-отчётов.
    /// Сверяется с живым списком стран тестом: разъедется — увидим, а не запишем чужие
    /// рекорды в <c>country/ISR</c>.
    /// </summary>
    public const string IsraelCountryId = "962f77d6-d9c0-49ad-ba93-adc831c9ec9f";

    /// <summary>Разбирает URL и проверяет домен по whitelist; иначе — понятная ошибка админу.</summary>
    public static Uri EnsureWhitelisted(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"Некорректный URL источника рекордов: '{url}'.");

        if (!string.Equals(uri.Host, ApiHost, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Домен '{uri.Host}' не в whitelist источников рекордов.");

        return uri;
    }

    /// <summary>
    /// То же для страницы-оглавления и файлов мастерских рекордов: они отдаются НЕ из API,
    /// а с сайта и из файлового хранилища. Отдельный предикат, а не расширение
    /// <see cref="EnsureWhitelisted"/>: отчётам рекордов эти хосты не нужны, и разрешать их
    /// там — молча расширять поверхность SSRF (docs/data-integrity.md §7, п.1).
    /// </summary>
    public static Uri EnsureDocumentWhitelisted(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"Некорректный URL источника рекордов: '{url}'.");

        if (!string.Equals(uri.Host, SiteHost, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Host, DocumentsHost, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Домен '{uri.Host}' не в whitelist источников рекордов.");

        return uri;
    }

    /// <summary>
    /// URL XLSX-отчёта рекордов. Хост и путь — константы, снаружи приходит только query;
    /// authority он изменить не может, но результат всё равно прогоняем через whitelist.
    /// </summary>
    public static Uri ReportUrl(string query) =>
        EnsureWhitelisted($"https://{ApiHost}/fina/records/report?{query}");

    /// <summary>
    /// URL JSON-выдачи рекордов — той, что питает страницу worldaquatics.com/swimming/records
    /// (<c>/fina/records/SW</c>, SW — плавание). Нужна для кодов, которые XLSX-отчёт не
    /// собирает за таймаут шлюза: <c>WJ</c> там — 504 (WJR-план, J0).
    /// </summary>
    public static Uri RecordsJsonUrl(string query) =>
        EnsureWhitelisted($"https://{ApiHost}/fina/records/SW?{query}");

    /// <summary>Список стран источника: GUID ↔ alpha-3 (11.1.1).</summary>
    public static Uri CountriesUrl => EnsureWhitelisted($"https://{ApiHost}/fina/countries");

    /// <summary>
    /// HTTP-клиент источника. Без User-Agent api.worldaquatics.com не отвечает вовсе —
    /// запрос висит до таймаута (проверено на приёмке 2.6); конкретное значение не важно.
    /// Таймаут задаёт вызывающий: отчёты отдаются 2–53 с (замер 15.09), список стран — за
    /// секунды.
    /// </summary>
    public static HttpClient CreateClient(IHttpClientFactory factory, TimeSpan timeout)
    {
        var client = factory.CreateClient();
        client.Timeout = timeout;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SwimmBot/1.0");
        return client;
    }
}
