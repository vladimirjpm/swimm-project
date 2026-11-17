namespace Swimm.API.Telemetry;

/// <summary>
/// Вычистка строки запроса перед отправкой телеметрии (docs/plans/application-insights-plan.md,
/// А2-бис). Чистые функции — без Activity и без ASP.NET, чтобы правило проверялось тестом построчно.
///
/// Правило:
/// <list type="bullet">
///   <item>у путей входа (<c>/auth/*</c>, <c>/signin-google</c>) строка запроса режется ЦЕЛИКОМ —
///     там одноразовый токен сброса пароля / подтверждения почты и OAuth-код Google;</item>
///   <item>у остальных ключи остаются, значения заменяются на <see cref="Placeholder"/>; уцелевает
///     только короткое число (id соревнования, номер страницы): в 8 цифр не влезают ни токен,
///     ни имя из поиска <c>?q=</c>, ни телефон, ни теудат-зеут (9 цифр).</item>
/// </list>
/// ⚠ <c>UseAzureMonitor()</c> САМ выключает встроенную вычистку инструментария ASP.NET Core и
/// HttpClient (ставит <c>OTEL_DOTNET_EXPERIMENTAL_*_DISABLE_URL_QUERY_REDACTION=True</c>, исходник
/// пакета 1.6.0). Мы её включаем обратно (<see cref="SwimmTelemetryExtensions"/>) — тогда она
/// успевает первой и заменяет на <c>Redacted</c> ВСЕ значения, числа тоже. Это правило — пол на
/// случай, если встроенную снова выключат обновлением пакета, плюс обрезка путей входа целиком.
/// </summary>
internal static class QueryStringRedactor
{
    public const string Placeholder = "Redacted";

    /// <summary>Сколько цифр может быть у значения, которое оставляем как есть.</summary>
    private const int MaxKeptDigits = 8;

    /// <summary>Ключ длиннее — тоже подозрительный (кто-то положил данные в имя параметра).</summary>
    private const int MaxKeptKeyLength = 32;

    /// <summary>Пути, у которых строка запроса не уходит наружу вовсе.</summary>
    private static readonly string[] SensitivePathPrefixes = ["/auth", "/signin-google"];

    /// <summary>
    /// Путь входа — строку запроса режем целиком. Сравнение по сегментам и без регистра,
    /// как <see cref="PathString.StartsWithSegments(PathString)"/>: <c>/AUTH/x</c> — тоже вход,
    /// <c>/authors</c> — нет.
    /// </summary>
    public static bool IsSensitivePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var p = new PathString(path.StartsWith('/') ? path : "/" + path);
        foreach (var prefix in SensitivePathPrefixes)
            if (p.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Строка запроса — с ведущим <c>?</c> или без (инструментарий кладёт в <c>url.query</c>
    /// по-разному в разных версиях). Возвращает null, если отправлять нечего.
    /// </summary>
    public static string? RedactQuery(string? query, bool dropAll)
    {
        if (string.IsNullOrEmpty(query)) return query;
        if (dropAll) return null;

        var hasMark = query[0] == '?';
        var body = hasMark ? query[1..] : query;
        if (body.Length == 0) return query;

        var parts = body.Split('&');
        for (var i = 0; i < parts.Length; i++)
            parts[i] = RedactPair(parts[i]);

        var result = string.Join('&', parts);
        return hasMark ? "?" + result : result;
    }

    /// <summary>
    /// Полный адрес (<c>https://host/path?q</c>) или цель запроса (<c>/path?q</c>). Фрагмент
    /// (<c>#…</c>) на сервер не приходит, но если он есть в теге — отбрасываем: в нём бывают токены.
    /// </summary>
    public static string RedactUrl(string url, bool dropQuery)
    {
        if (string.IsNullOrEmpty(url)) return url;

        var hash = url.IndexOf('#');
        if (hash >= 0) url = url[..hash];

        var mark = url.IndexOf('?');
        if (mark < 0) return url;

        var prefix = url[..mark];
        if (dropQuery || IsSensitivePath(PathOf(prefix))) return prefix;

        var query = RedactQuery(url[mark..], dropAll: false);
        return prefix + query;
    }

    private static string RedactPair(string pair)
    {
        if (pair.Length == 0) return pair;

        var eq = pair.IndexOf('=');
        // «?SEKRET» без ключа — само значение; оставляем только короткое число.
        if (eq < 0) return IsSafeValue(pair) ? pair : Placeholder;

        var key = pair[..eq];
        var value = pair[(eq + 1)..];
        if (key.Length > MaxKeptKeyLength) return Placeholder;
        return value.Length == 0 || IsSafeValue(value) ? pair : key + "=" + Placeholder;
    }

    private static bool IsSafeValue(string value)
    {
        if (value.Length == 0 || value.Length > MaxKeptDigits) return false;
        foreach (var c in value)
            if (c < '0' || c > '9') return false;
        return true;
    }

    /// <summary>Путь из адреса без строки запроса: абсолютный URL или уже путь.</summary>
    private static string PathOf(string urlWithoutQuery) =>
        Uri.TryCreate(urlWithoutQuery, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri.AbsolutePath
            : urlWithoutQuery;
}
