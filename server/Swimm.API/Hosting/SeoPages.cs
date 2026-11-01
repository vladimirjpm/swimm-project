using System.Collections.Concurrent;
using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
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
/// Тело (Search Console 01.11.2026: 1 347 адресов «обнаружены, не проиндексированы»): в пустой
/// корень индексируемой страницы, главной и <c>/competitions</c> кладётся короткий текст со
/// ссылками на связанные страницы — без него краулер видел адреса только в sitemap. React
/// при первом рендере заменяет содержимое корня своим; пока скрипт грузится, блок спрятан
/// правилом <c>@media (scripting: enabled)</c> — его видят только те, кто скрипт не выполняет.
///
/// Чего здесь нет: локально клиента в wwwroot нет (он на Vite), и подстановка молча не
/// срабатывает — страница отдаётся как раньше. Проверять — на publish-сборке.
/// </summary>
public static class SeoPages
{
    public const string BaseUrlKey = "Seo:BaseUrl";
    public const string DefaultBaseUrl = "https://swimhub.co.il";

    public enum Kind { Swimmer, Club, Competition, NoIndexOnly, Home, CompetitionsIndex }

    /// <summary>Какая страница открыта по чистому пути. null — подставлять нечего.</summary>
    public sealed record Target(Kind Kind, int Id = 0);

    public static Target? Match(string path)
    {
        var seg = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (seg.Length == 0) return new Target(Kind.Home);
        if (seg.Length == 1) return seg[0] == "competitions" ? new Target(Kind.CompetitionsIndex) : null;

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

    /// <summary>
    /// Что вставить в страницу. <see cref="Head"/> null — заголовок остаётся из html;
    /// <see cref="Body"/> null — корень остаётся пустым.
    /// </summary>
    public sealed record Page(SeoHead? Head, SeoBody? Body, bool NoIndex);

    private static readonly Page NoIndexPage = new(null, null, true);

    /// <summary>
    /// Голова и тело для цели. Сущности нет — <c>noindex</c> (страница сама покажет «не найдено»).
    /// Тело — только у индексируемых: у страницы ребёнка, скрытого пловца, псевдо- и склеенного
    /// клуба его нет, лишних имён в HTML тоже.
    /// </summary>
    public static async Task<Page> BuildPageAsync(Target target, ISeoRepository repo, CancellationToken ct)
    {
        switch (target.Kind)
        {
            case Kind.Home:
                return new Page(null, SeoText.HomeBody(await repo.GetCompetitionsAsync(ct)), false);
            case Kind.CompetitionsIndex:
                return new Page(null, SeoText.CompetitionsBody(await repo.GetCompetitionsAsync(ct)), false);
            case Kind.Swimmer when await repo.GetSwimmerAsync(target.Id, ct) is { } s:
            {
                var head = SeoText.Swimmer(s, IsraelTime.ToLocal(DateTime.UtcNow).Year);
                return head.NoIndex ? new Page(head, null, true)
                    : new Page(head, SeoText.SwimmerBody(s, head, await repo.GetSwimmerLinksAsync(s.Id, ct)), false);
            }
            case Kind.Club when await repo.GetClubAsync(target.Id, ct) is { } c:
            {
                var head = SeoText.Club(c);
                return head.NoIndex ? new Page(head, null, true)
                    : new Page(head, SeoText.ClubBody(c, head, await repo.GetClubLinksAsync(c.Id, ct)), false);
            }
            case Kind.Competition when await repo.GetCompetitionAsync(target.Id, ct) is { } c:
            {
                var head = SeoText.Competition(c);
                return head.NoIndex ? new Page(head, null, true)
                    : new Page(head, SeoText.CompetitionBody(c, head, await repo.GetCompetitionLinksAsync(c.Id, ct)), false);
            }
            default:
                return NoIndexPage;
        }
    }

    private static readonly Regex TitleRx = new(@"<title>.*?</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RobotsRx = new(@"\s*<meta\s+name=""robots""[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Корень страницы — первый пустой div с id (#root, #club-page…): его заполняет React.
    private static readonly Regex EmptyRootRx = new(@"<div\s+id=""[\w-]+""\s*>\s*</div>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Пока работает скрипт, тело не показываем: React через миг заменит его настоящей страницей,
    /// а мелькание голого списка выглядело бы поломкой. Без скрипта (и для краулера, который
    /// читает ссылки из HTML) блок на месте.
    /// </summary>
    internal const string BodyStyle = "<style>@media (scripting: enabled) { .seo-static { display: none } }</style>";

    /// <summary>
    /// Вставить голову в html: заменить <c>&lt;title&gt;</c>, снять статичный robots, дописать перед
    /// <c>&lt;/head&gt;</c> описание, canonical, og-метки и robots. head == null — только
    /// <c>noindex</c>. body — в пустой корень страницы (нет корня — тело не вставляется).
    /// Всё из базы — через <see cref="Encode"/>: имена приходят из протоколов.
    /// </summary>
    public static string Inject(string html, SeoHead? head, bool noIndex, string baseUrl, SeoBody? body = null)
    {
        var enc = Encode;
        var sb = new StringBuilder();
        if (body is not null && EmptyRootRx.Match(html) is { Success: true } root)
        {
            var open = root.Value.IndexOf('>') + 1;
            html = html.Insert(root.Index + open, RenderBody(body));
            sb.Append("\n    ").Append(BodyStyle);
        }
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

    /// <summary>Постоянные ссылки внизу тела: с любой страницы краулер дойдёт до разделов сайта.</summary>
    private static readonly (string Path, string Text)[] FooterLinks =
    [
        ("/", SeoText.SiteName), ("/competitions", "Competitions"), ("/records", "Records"),
        ("/season-best", "Season bests"), ("/about", "About"),
    ];

    /// <summary>
    /// Тело — простая разметка без классов оформления: её видит только тот, кто не выполняет
    /// скрипт. <c>dir="auto"</c> — имена ивритские, обвязка английская.
    /// </summary>
    public static string RenderBody(SeoBody body)
    {
        var enc = Encode;
        var sb = new StringBuilder("\n<div class=\"seo-static\">");
        sb.Append($"\n<h1 dir=\"auto\">{enc(body.Heading)}</h1>");
        if (!string.IsNullOrWhiteSpace(body.Lead))
            sb.Append($"\n<p dir=\"auto\">{enc(body.Lead)}</p>");
        foreach (var section in body.Sections)
        {
            if (section.Links.Count == 0) continue;
            sb.Append($"\n<h2>{enc(section.Title)}</h2>\n<ul>");
            foreach (var link in section.Links)
            {
                sb.Append($"\n<li><a href=\"{enc(link.Path)}\" dir=\"auto\">{enc(link.Text)}</a>");
                if (!string.IsNullOrWhiteSpace(link.Note)) sb.Append($" · {enc(link.Note)}");
                sb.Append("</li>");
            }
            sb.Append("\n</ul>");
            if (section.More > 0)
                sb.Append($"\n<p>and {section.More.ToString("N0", CultureInfo.InvariantCulture)} more</p>");
        }
        sb.Append("\n<nav>")
          .AppendJoin(" · ", FooterLinks.Select(l => $"<a href=\"{l.Path}\">{enc(l.Text)}</a>"))
          .Append("</nav>\n</div>\n");
        return sb.ToString();
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

        Page page;
        try
        {
            var repo = context.RequestServices.GetRequiredService<ISeoRepository>();
            page = await BuildPageAsync(target, repo, context.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // База лежит — страница всё равно должна открыться: отдаём без подстановки.
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SeoPages))
                .LogWarning(ex, "SEO: не собрал заголовок для {Path}, отдаю страницу как есть", cleanPath);
            return false;
        }

        var key = $"{file.PhysicalPath}|{file.LastModified.UtcTicks}";
        var html = HtmlCache.GetOrAdd(key, _ => File.ReadAllText(file.PhysicalPath, Encoding.UTF8));

        var config = context.RequestServices.GetRequiredService<IConfiguration>();
        var result = Inject(html, page.Head, page.NoIndex, config[BaseUrlKey] ?? DefaultBaseUrl, page.Body);

        context.Response.ContentType = "text/html; charset=utf-8";
        // Голова зависит от данных — браузер переспрашивает документ каждый раз (он маленький).
        context.Response.Headers.CacheControl = "no-cache";
        if (page.NoIndex) context.Response.Headers["X-Robots-Tag"] = "noindex";
        await context.Response.WriteAsync(result, context.RequestAborted);
        return true;
    }

    /// <summary>
    /// Служебные пути, которые отвечают на ЛЮБОМ хосте: health check App Service и дымовая проверка
    /// деплоя (<c>azure-deploy.yml</c>) ходят на <c>*.azurewebsites.net</c> и ждут 200, а не 301.
    /// </summary>
    private static readonly string[] ServicePaths = ["/healthz", "/readyz", "/version"];

    /// <summary>
    /// Куда увести запрос на «не тот» хост: <c>www.swimhub.co.il/x?y</c> и
    /// <c>swimhub.azurewebsites.net/x?y</c> → <c>https://swimhub.co.il/x?y</c> (301). У сайта один
    /// адрес — одна страница в выдаче, одна кука входа, один redirect URI у Google (15.10.2026: вход
    /// на azurewebsites.net не давал входа на swimhub.co.il — куки у адресов свои).
    /// null — уводить не надо: сам canonical-хост, служебные пути на любом хосте, прочие хосты
    /// (localhost, ProdAdmin — их этот код и не видит).
    /// </summary>
    public static string? CanonicalRedirect(string host, string path, string pathAndQuery, string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var canonical)) return null;
        if (ServicePaths.Any(p => string.Equals(path, p, StringComparison.OrdinalIgnoreCase))) return null;

        var isWww = string.Equals(host, "www." + canonical.Host, StringComparison.OrdinalIgnoreCase);
        var isAzureDefault = host.EndsWith(".azurewebsites.net", StringComparison.OrdinalIgnoreCase);
        return isWww || isAzureDefault
            ? $"{canonical.Scheme}://{canonical.Host}{pathAndQuery}"
            : null;
    }

    /// <summary>Публичные страницы без сущности — в sitemap.xml целиком. Группы, my-*, h2h — нет.</summary>
    public static readonly IReadOnlyList<string> StaticSitemapPaths =
    [
        "/", "/competitions", "/season-best", "/records", "/about", "/privacy", "/terms", "/help/groups",
    ];

    /// <summary>
    /// Все адреса sitemap.xml: статичные страницы, затем сущности из базы. Главная и
    /// <c>/competitions</c> показывают последние соревнования — их lastmod = самый свежий старт;
    /// у остальных статичных даты нет (меняются с деплоем, а не с данными).
    /// </summary>
    public static IEnumerable<SeoSitemapEntry> SitemapEntries(IReadOnlyList<SeoSitemapEntry> entities)
    {
        var latest = entities.Max(e => e.LastModified);
        return StaticSitemapPaths
            .Select(p => new SeoSitemapEntry(p, p is "/" or "/competitions" ? latest : null))
            .Concat(entities);
    }

    /// <summary>
    /// sitemap.xml. Адреса — на canonical-хост, как в голове страниц. Дата из будущего (кривой
    /// протокол) в <c>&lt;lastmod&gt;</c> не идёт: такие Google считает враньём и перестаёт верить
    /// датам сайта целиком.
    /// </summary>
    public static string BuildSitemap(IEnumerable<SeoSitemapEntry> entries, string baseUrl, DateOnly today)
    {
        var root = baseUrl.TrimEnd('/');
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        foreach (var e in entries)
        {
            sb.Append("  <url><loc>").Append(SecurityElement.Escape(root + e.Path)).Append("</loc>");
            if (e.LastModified is { } d && d <= today)
                sb.Append("<lastmod>").Append(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("</lastmod>");
            sb.Append("</url>\n");
        }
        sb.Append("</urlset>\n");
        return sb.ToString();
    }
}
