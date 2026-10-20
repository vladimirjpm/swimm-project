namespace Swimm.Application.Mapping;

/// <summary>
/// Правила push-подписок (docs/plans/web-push-reminders-plan.md §6) — чистые функции: какой адрес
/// подписки принимаем, как подписать устройство в списке, сколько устройств на человека.
/// </summary>
public static class PushRules
{
    /// <summary>Устройств (браузеров) на аккаунт; лишнее вытесняет самое старое.</summary>
    public const int MaxDevicesPerUser = 10;

    public const int EndpointMax = 1000;
    public const int KeyMax = 200;
    public const int AuthMax = 100;

    /// <summary>Политика rate limit для «Send test» и подписок (Program.cs).</summary>
    public const string RateLimitPolicy = "push";

    public const int RateLimitPerMinute = 10;

    /// <summary>
    /// Хосты push-служб браузеров. Сервер сам шлёт POST на адрес подписки — поэтому принимаем
    /// только https и только эти службы: Chrome / Edge / Samsung (FCM), Safari (Apple), Firefox
    /// (Mozilla), Edge на Windows (WNS).
    /// </summary>
    private static readonly string[] AllowedHostSuffixes =
    [
        "fcm.googleapis.com",
        "android.googleapis.com",
        "push.apple.com",
        "push.services.mozilla.com",
        "notify.windows.com",
    ];

    /// <summary>Адрес подписки годится: https, без порта и логина, хост — известная push-служба.</summary>
    public static bool IsAllowedEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > EndpointMax) return false;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return false;
        var host = uri.IdnHost.ToLowerInvariant();
        return AllowedHostSuffixes.Any(s => host == s || host.EndsWith("." + s, StringComparison.Ordinal));
    }

    /// <summary>Ключ подписки — base64url без мусора (p256dh ~87 символов, auth ~22).</summary>
    public static bool IsBase64Url(string? value, int max) =>
        !string.IsNullOrEmpty(value) && value.Length <= max
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '=');

    /// <summary>
    /// «iPhone · Safari», «Android · Chrome», «Windows · Edge» — для списка устройств. Точность
    /// не нужна: человеку надо узнать свой телефон, а не версию движка.
    /// </summary>
    public static string DeviceLabel(string? userAgent)
    {
        var ua = userAgent ?? "";
        bool Has(string s) => ua.Contains(s, StringComparison.OrdinalIgnoreCase);

        var os = Has("iPhone") ? "iPhone"
            : Has("iPad") ? "iPad"
            : Has("Android") ? "Android"
            : Has("Windows") ? "Windows"
            : Has("Mac OS X") || Has("Macintosh") ? "Mac"
            : Has("Linux") ? "Linux"
            : "Device";
        var browser = Has("Edg/") || Has("EdgA/") ? "Edge"
            : Has("SamsungBrowser") ? "Samsung Internet"
            : Has("OPR/") ? "Opera"
            : Has("Firefox/") || Has("FxiOS") ? "Firefox"
            : Has("CriOS") || Has("Chrome/") ? "Chrome"
            : Has("Safari/") ? "Safari"
            : null;
        return browser is null ? os : $"{os} · {browser}";
    }
}
