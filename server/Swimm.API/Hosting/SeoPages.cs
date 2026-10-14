using System.Collections.Concurrent;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.API.Hosting;

/// <summary>
/// Открытие сайта для поиска (A10, решение В16): серверная «голова» страниц витрины и
/// <c>sitemap.xml</c>.
///
/// Страницы рисуются в браузере — в голом HTML <c>swimmer.html</c> только <c>&lt;title&gt;Swimmer</c> и
/// пустой корень. Поисковик скрипт выполнит не сразу, а ИИ-агенты обычно не выполняют вовсе.
/// Поэтому прослойка чистых URL (Program.cs), узнав страницу пловца/клуба/соревнования, отдаёт
/// html с подставленными <c>&lt;title&gt;</c>, описанием, canonical и og-метками. Тексты — в
/// <see cref="SeoText"/>; здесь только «какая страница» и «как вставить».
///
/// Чего здесь нет: локально клиента в wwwroot нет (он на Vite), и подстановка молча не
/// срабатывает — страница отдаётся как раньше. Проверять — на publish-сборке.
/// </summary>
public static class SeoPages
{
    public const string BaseUrlKey = "Seo:BaseUrl";
    public const string DefaultBaseUrl = "https://swimhub.co.il";

    public enum Kind { Swimmer, Club, Competition, NoIndexOnly }

    /// <summary>Какая страница открыта по чистому пути. null — подставлять нечего.</summary>
    public sealed record Target(Kind Kind, int Id = 0);

    public static Target? Match(string path)
    {
        var seg = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (seg.Length < 2) return null;

        return seg[0] switch
        {
            "swimmers" when seg.Length == 2 => ParseId(seg[1]) is { } id ? new Target(Kind.Swimmer, id) : null,
            "clubs" when seg.Length == 2 => ParseId(seg[1]) is { } id ? new Target(Kind.Club, id) : null,
            // /competitions/last и стартовые протоколы /competitions/upcoming/{orgId} — адреса без
            // постоянного содержимого: у «last» оно меняется с каждым импортом, у стартового
            // протокола живёт до протокола. В выдаче им делать нечего, ссылки с них — пусть ходят.
            "competitions" when seg.Length == 2 && seg[1] == "last" => new Target(Kind.NoIndexOnly),
            "competitions" when seg[1] == "upcoming" => new Target(Kind.NoIndexOnly),
            "competitions" when seg.Length == 2 => ParseId(seg[1]) is { } id ? new Target(Kind.Competition, id) : null,
            // Результаты ростера группы рисует тот же results_main.html, что и соревнование, —
            // noindex со страницы снят, поэтому группам он ставится здесь (группы закрыты всегда).
            "groups" => new Target(Kind.NoIndexOnly),
            _ => null,
        };
    }

    private static int? ParseId(string s) => int.TryParse(s, out var id) && id > 0 ? id : null;

    /// <summary>Head для цели; null — сущности нет (страница сама покажет «не найдено»).</summary>
    public static async Task<SeoHead?> BuildHeadAsync(Target target, ISeoRepository repo, CancellationToken ct) =>
        target.Kind switch
        {
            Kind.Swimmer => await repo.GetSwimmerAsync(target.Id, ct) is { } s ? SeoText.Swimmer(s) : null,
            Kind.Club => await repo.GetClubAsync(target.Id, ct) is { } c ? SeoText.Club(c) : null,
            Kind.Competition => await repo.GetCompetitionAsync(target.Id, ct) is { } c ? SeoText.Competition(c) : null,
            _ => null,
        };

    private static readonly Regex TitleRx = new(@"<title>.*?</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RobotsRx = new(@"\s*<meta\s+name=""robots""[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Вставить голову в html: заменить <c>&lt;title&gt;</c>, снять статичный robots, дописать перед
    /// <c>&lt;/head&gt;</c> описание, canonical, og-метки и robots. head == null — только
    /// <c>noindex</c>. Всё из базы — через <see cref="Encode"/>: имена приходят из протоколов.
    /// </summary>
    public static string Inject(string html, SeoHead? head, bool noIndex, string baseUrl)
    {
        var enc = Encode;
        var sb = new StringBuilder();
        if (head is not null)
        {
            var url = baseUrl.TrimEnd('/') + head.CanonicalPath;
            sb.Append($"\n    <meta name=\"description\" content=\"{enc(head.Description)}\" />");
            sb.Append($"\n    <link rel=\"canonical\" href=\"{enc(url)}\" />");
            sb.Append($"\n    <meta property=\"og:type\" content=\"website\" />");
            sb.Append($"\n    <meta property=\"og:site_name\" content=\"{SeoText.SiteName}\" />");
            sb.Append($"\n    <meta property=\"og:title\" content=\"{enc(head.Title)}\" />");
            sb.Append($"\n    <meta property=\"og:description\" content=\"{enc(head.Description)}\" />");
            sb.Append($"\n    <meta property=\"og:url\" content=\"{enc(url)}\" />");
            // Evaluator, а не строка замены: в строке «$1» из названия стал бы подстановкой.
            var title = $"<title>{enc(head.Title)}</title>";
            html = TitleRx.Replace(html, _ => title, 1);
        }
        if (noIndex)
            sb.Append("\n    <meta name=\"robots\" content=\"noindex, follow\" />");

        html = RobotsRx.Replace(html, "");
        var at = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return at < 0 ? html : html.Insert(at, sb.Append('\n').ToString());
    }

    /// <summary>
    /// Экранирование для текста и атрибута в двойных кавычках — пять символов, и всё. Не
    /// WebUtility.HtmlEncode: тот превращает «·» и прочую латиницу-1 в &amp;#183;, а иврит оставляет —
    /// разнобой в голове страницы без пользы.
    /// </summary>
    internal static string Encode(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&#39;");

    // Файлы сборки не меняются, пока процесс жив (деплой = рестарт). Ключ — путь + время файла:
    // локальная пересборка клиента в wwwroot тоже подхватится.
    private static readonly ConcurrentDictionary<string, string> HtmlCache = new();

    /// <summary>
    /// Отдать страницу с подставленной головой. false — не наша страница или html нет в wwwroot
    /// (локально клиент живёт на Vite): вызывающий отдаёт файл как обычно.
    /// </summary>
    public static async Task<bool> TryServeAsync(HttpContext context, string cleanPath, string htmlFile)
    {
        if (!HttpMethods.IsGet(context.Request.Method)) return false;
        var target = Match(cleanPath);
        if (target is null) return false;

        var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var file = env.WebRootFileProvider.GetFileInfo(htmlFile);
        if (!file.Exists || file.PhysicalPath is null) return false;

        SeoHead? head = null;
        if (target.Kind != Kind.NoIndexOnly)
        {
            try
            {
                var repo = context.RequestServices.GetRequiredService<ISeoRepository>();
                head = await BuildHeadAsync(target, repo, context.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // База лежит — страница всё равно должна открыться: отдаём без подстановки.
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SeoPages))
                    .LogWarning(ex, "SEO: не собрал заголовок для {Path}, отдаю страницу как есть", cleanPath);
                return false;
            }
        }
        // Сущности нет — страница скажет «не найдено» сама; в выдаче ей делать нечего.
        var noIndex = target.Kind == Kind.NoIndexOnly || head is null || head.NoIndex;

        var key = $"{file.PhysicalPath}|{file.LastModified.UtcTicks}";
        var html = HtmlCache.GetOrAdd(key, _ => File.ReadAllText(file.PhysicalPath, Encoding.UTF8));

        var config = context.RequestServices.GetRequiredService<IConfiguration>();
        var body = Inject(html, head, noIndex, config[BaseUrlKey] ?? DefaultBaseUrl);

        context.Response.ContentType = "text/html; charset=utf-8";
        // Голова зависит от данных — браузер переспрашивает документ каждый раз (он маленький).
        context.Response.Headers.CacheControl = "no-cache";
        if (noIndex) context.Response.Headers["X-Robots-Tag"] = "noindex";
        await context.Response.WriteAsync(body, context.RequestAborted);
        return true;
    }

    /// <summary>
    /// Куда увести запрос на <c>www.</c>-хост canonical-адреса: <c>www.swimhub.co.il/x?y</c> →
    /// <c>https://swimhub.co.il/x?y</c> (301). null — уводить не надо (другой хост: сам
    /// swimhub.co.il, *.azurewebsites.net для health check и дымовой проверки деплоя, localhost).
    /// Один адрес сайта — одна страница в выдаче и один redirect URI у Google.
    /// </summary>
    public static string? WwwRedirect(string host, string pathAndQuery, string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var canonical)) return null;
        return string.Equals(host, "www." + canonical.Host, StringComparison.OrdinalIgnoreCase)
            ? $"{canonical.Scheme}://{canonical.Host}{pathAndQuery}"
            : null;
    }

    /// <summary>Публичные страницы без сущности — в sitemap.xml целиком. Группы, my-*, h2h — нет.</summary>
    public static readonly IReadOnlyList<string> StaticSitemapPaths =
    [
        "/", "/competitions", "/season-best", "/records", "/about", "/privacy", "/terms", "/help/groups",
    ];

    /// <summary>sitemap.xml по списку путей. Адреса — на canonical-хост, как в голове страниц.</summary>
    public static string BuildSitemap(IEnumerable<string> paths, string baseUrl)
    {
        var root = baseUrl.TrimEnd('/');
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        foreach (var p in paths)
            sb.Append("  <url><loc>").Append(SecurityElement.Escape(root + p)).Append("</loc></url>\n");
        sb.Append("</urlset>\n");
        return sb.ToString();
    }
}
