using System.Diagnostics;
using OpenTelemetry;

namespace Swimm.API.Telemetry;

/// <summary>
/// Вычищает адреса во ВСЕХ спанах (входящие запросы, исходящий HttpClient) по правилу
/// <see cref="QueryStringRedactor"/>. Зовётся дважды, и это намеренно:
/// <list type="number">
///   <item>из <c>EnrichWithHttpRequest</c> инструментария ASP.NET Core — сразу после того, как он
///     положил теги, то есть раньше любого экспортёра (порядок процессоров тут не важен);</item>
///   <item>как процессор в <c>OnEnd</c> — для спанов, у которых обогащения нет (HttpClient и др.).
///     Регистрируется ДО <c>UseAzureMonitor()</c>, чтобы стоять в цепочке перед экспортёром.</item>
/// </list>
/// </summary>
internal sealed class TelemetryRedactionProcessor : BaseProcessor<Activity>
{
    // Новые (url.*) и старые (http.*) семантические соглашения: экспортёр Azure Monitor
    // собирает адрес запроса из любого набора, поэтому чистим все.
    internal const string UrlQueryTag = "url.query";
    internal const string UrlPathTag = "url.path";
    internal static readonly string[] UrlTags = ["url.full", "http.url", "http.target"];

    public override void OnEnd(Activity activity) => Redact(activity);

    internal static void Redact(Activity activity)
    {
        var dropQuery = QueryStringRedactor.IsSensitivePath(activity.GetTagItem(UrlPathTag) as string);

        if (activity.GetTagItem(UrlQueryTag) is string query && query.Length > 0)
        {
            var redacted = QueryStringRedactor.RedactQuery(query, dropQuery);
            if (redacted != query) activity.SetTag(UrlQueryTag, redacted); // null убирает тег
        }

        foreach (var tag in UrlTags)
        {
            if (activity.GetTagItem(tag) is not string url || url.Length == 0) continue;
            var redacted = QueryStringRedactor.RedactUrl(url, dropQuery);
            if (redacted != url) activity.SetTag(tag, redacted);
        }
    }
}
