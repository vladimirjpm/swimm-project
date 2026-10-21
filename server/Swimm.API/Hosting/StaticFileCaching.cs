namespace Swimm.API.Hosting;

/// <summary>
/// Заголовок <c>Cache-Control</c> для статики витрины (wwwroot = сборка Vite).
///
/// Без заголовка браузер кэширует файл «эвристически» (доля от возраста <c>Last-Modified</c>) и
/// после деплоя продолжает отдавать старую версию. Поймано 21.10.2026 на проде: после выкладки
/// 0461851b браузер держал прошлый <c>results_main.js</c>, и починка ссылки <c>?plan=</c> не
/// доезжала, пока кэш не сбросили руками. Хуже со старым <c>.html</c>: он ссылается на
/// хэшированные чанки прошлой сборки, а их после деплоя на сервере уже нет — страница ломается.
///
/// Поэтому всё, у чего имя НЕ меняется от сборки к сборке, отдаём с <c>no-cache</c>: браузер
/// хранит копию, но перед использованием сверяет ETag (304 без тела, если не менялось):
/// <list type="bullet">
/// <item><c>*.html</c> — страницы (в том числе отданные через чистые URL и <c>/</c> → <c>home.html</c>:
/// оба пути переписывают <c>Request.Path</c> до статики);</item>
/// <item><c>*.js</c> вне <c>/assets/</c> — входные точки Vite (<c>entryFileNames: '[name].js'</c>),
/// <c>sw.js</c>, <c>data/filter-data.js</c>.</item>
/// </list>
/// Чанки в <c>/assets/</c> несут хэш в имени — новая сборка = новое имя, их не трогаем.
/// </summary>
public static class StaticFileCaching
{
    public const string NoCache = "no-cache";

    /// <summary>Значение <c>Cache-Control</c> для файла по пути запроса; null — заголовок не ставим.</summary>
    public static string? CacheControlFor(string? requestPath)
    {
        if (string.IsNullOrEmpty(requestPath)) return null;

        if (requestPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) return NoCache;

        if (requestPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            && !requestPath.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
            return NoCache;

        return null;
    }
}
