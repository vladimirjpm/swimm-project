namespace Swimm.Parsing.RecordSources;

/// <summary>
/// Общее для обоих isr.org.il-источников рекордов (age и masters): whitelist домена и
/// скачивание файла. Проверка домена — предикат безопасности (SSRF), поэтому живёт ровно
/// в одном месте: копия предиката = будущий инцидент (docs/data-integrity.md §7, п.1).
/// </summary>
public static class IsrOrgRecordsSource
{
    /// <summary>Страница «שיאי ישראל» со ссылками на все четыре PDF (age/masters × 50m/25m).</summary>
    public const string RecordsPageUrlDefault = "https://isr.org.il/data.asp?id=1013";

    private const string AllowedHost = "isr.org.il";
    private const string AllowedHostSuffix = ".isr.org.il";

    /// <summary>Разбирает URL и проверяет домен по whitelist; иначе — понятная ошибка админу.</summary>
    public static Uri EnsureWhitelisted(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"Некорректный URL источника рекордов: '{url}'.");

        if (!string.Equals(uri.Host, AllowedHost, StringComparison.OrdinalIgnoreCase)
            && !uri.Host.EndsWith(AllowedHostSuffix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Домен '{uri.Host}' не в whitelist источников рекордов.");

        return uri;
    }

    /// <summary>Скачивает файл в память с проверкой домена. Поток отдаётся с позиции 0.</summary>
    public static async Task<MemoryStream> FetchWhitelistedAsync(
        HttpClient client, string url, CancellationToken ct)
    {
        var uri = EnsureWhitelisted(url);

        var response = await client.GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();

        var ms = new MemoryStream();
        await response.Content.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    /// <summary>HTTP-клиент для isr.org.il: без User-Agent часть источников не отвечает вовсе.</summary>
    public static HttpClient CreateClient(IHttpClientFactory factory)
    {
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SwimmBot/1.0");
        return client;
    }
}
