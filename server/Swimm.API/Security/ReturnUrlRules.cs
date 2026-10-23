namespace Swimm.API.Security;

/// <summary>
/// Куда можно вернуть после входа и выхода (<c>/auth/login/google?returnUrl=</c>,
/// <c>/auth/logout?returnUrl=</c>). До 23.10.2026 там стоял голый <c>Redirect(returnUrl)</c> —
/// открытый редирект: ссылка на НАШ вход уводила после него на чужой сайт (фишинг «войдите
/// ещё раз»). Найдено ревью безопасности удаления аккаунта, docs/plans/account-deletion-plan.md §7.
///
/// Разрешено:
///  - путь своего сайта: <c>/x?y#z</c> (не <c>//host</c> и не <c>/\host</c> — браузер читает их
///    как адрес другого сайта);
///  - полный http(s)-адрес, если хост — хост самого запроса, канонический хост сайта
///    (<c>Seo:BaseUrl</c>) или, на машине разработчика, localhost. Полный адрес нужен: клиент
///    шлёт <c>window.location.href</c>, а в деве страница живёт на порту Vite (5173/5181), не на
///    порту API — прокси Vite подменяет хост (<c>changeOrigin</c>), так что сверка по порту
///    сломала бы вход. Порт поэтому не сравниваем: другой порт того же хоста — та же машина.
/// Всё прочее — главная <c>/</c>.
/// </summary>
public static class ReturnUrlRules
{
    public const string Fallback = "/";

    private static readonly string[] LocalHosts = ["localhost", "127.0.0.1", "[::1]", "::1"];

    /// <param name="returnUrl">Что пришло в запросе.</param>
    /// <param name="requestHost">Хост запроса без порта (<c>Request.Host.Host</c>).</param>
    /// <param name="canonicalHost">Хост из <c>Seo:BaseUrl</c> (swimhub.co.il); null — нет.</param>
    /// <param name="allowLocalhost">Машина разработчика (Development / ProdAdmin).</param>
    public static string Safe(string? returnUrl, string requestHost, string? canonicalHost, bool allowLocalhost)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return Fallback;
        var url = returnUrl.Trim();

        // Управляющие символы (CR/LF — подмена заголовков) и обратный слэш (браузеры читают его как
        // «/», и /\evil.com становится //evil.com) — сразу на главную.
        if (url.Any(c => char.IsControl(c) || c == '\\')) return Fallback;

        if (url[0] == '/')
            return url.Length > 1 && url[1] == '/' ? Fallback : url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return Fallback;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return Fallback;
        // https://swimhub.co.il@evil.com — хост здесь evil.com, но и с «userinfo» на своём хосте
        // такому адресу у нас взяться неоткуда.
        if (!string.IsNullOrEmpty(uri.UserInfo)) return Fallback;

        var host = uri.Host;
        var allowed = Same(host, requestHost)
            || (canonicalHost != null && Same(host, canonicalHost))
            || (allowLocalhost && LocalHosts.Any(h => Same(host, h)));
        return allowed ? uri.AbsoluteUri : Fallback;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
