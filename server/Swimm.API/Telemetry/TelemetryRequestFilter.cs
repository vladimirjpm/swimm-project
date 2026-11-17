namespace Swimm.API.Telemetry;

/// <summary>
/// Какие входящие запросы НЕ пишем в телеметрию (план А2.4). Health-check App Service стучит в
/// <c>/healthz</c> раз в минуту, а страница тянет десятки файлов статики — без фильтра это
/// съело бы дневной лимит приёма и утопило бы настоящие запросы.
///
/// Фильтр смотрит на ИСХОДНЫЙ путь — инструментарий зовёт его до переписывания чистых URL
/// (Program.cs), поэтому <c>/competitions/1581</c> сюда приходит как есть и пишется.
/// </summary>
internal static class TelemetryRequestFilter
{
    private static readonly string[] SkippedPaths = ["/healthz", "/readyz", "/version"];

    // Папки статики клиента (Vite кладёт сборку в /assets) и админки.
    private static readonly string[] SkippedPrefixes = ["/assets", "/images", "/css", "/js", "/data", "/fonts"];

    /// <summary>true — запрос пишем.</summary>
    public static bool ShouldRecord(PathString path)
    {
        if (!path.HasValue) return true;
        // API пишем всегда, даже если у ручки в адресе «расширение» (выгрузки *.csv и т.п.).
        if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var p in SkippedPaths)
            if (path.Equals(p, StringComparison.OrdinalIgnoreCase)) return false;

        foreach (var p in SkippedPrefixes)
            if (path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)) return false;

        // Отдельные файлы в корне: favicon.ico, sw.js, manifest.json, robots.txt, sitemap.xml.
        // Страницы (.html) оставляем — это и есть заходы на сайт; у API расширения нет.
        var last = path.Value!.AsSpan(path.Value!.LastIndexOf('/') + 1);
        var dot = last.LastIndexOf('.');
        if (dot < 0) return true;
        return last[dot..].Equals(".html", StringComparison.OrdinalIgnoreCase);
    }
}
