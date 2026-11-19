using System.Net;
using System.Text.RegularExpressions;

namespace Swimm.API.Telemetry;

/// <summary>
/// Кто прислал запрос — тег <see cref="Tag"/> на спане запроса (в Azure — <c>AppRequests.Properties</c>).
/// Решение Влада 19.11.2026: отделить наши служебные запросы и роботов от посетителей, чтобы, когда
/// людей станет много, медленное и сломанное «у людей» не тонуло в собственных проверках.
/// <list type="bullet">
/// <item><see cref="Warmup"/> — наш прогрев после старта (<c>StartupWarmupService</c>): заголовок
///   <see cref="WarmupHeader"/> И соединение с самой машины. Снаружи заголовок ничего не даёт: от
///   App Service соединение приходит не с loopback.</item>
/// <item><see cref="Uptime"/> — внешний мониторинг (UptimeRobot; он ходит на <c>/readyz</c>, который
///   и так не пишется, — метка на случай проверок других адресов).</item>
/// <item><see cref="Bot"/> — поисковые и прочие роботы по User-Agent.</item>
/// <item><see cref="Visitor"/> — всё остальное: люди (и роботы, которые не представились).</item>
/// </list>
/// Метка — не защита: User-Agent подделывается. Она для статистики, прав по ней не даём.
/// </summary>
internal static partial class RequestSource
{
    internal const string Tag = "swimm.source";

    internal const string Warmup = "warmup";
    internal const string Uptime = "uptime";
    internal const string Bot = "bot";
    internal const string Visitor = "visitor";

    internal const string WarmupHeader = "X-Swimm-Warmup";

    /// <param name="remoteIp">Адрес соединения ДО <c>UseForwardedHeaders</c> (инструментарий видит запрос
    /// раньше middleware) — подменить его заголовком X-Forwarded-For нельзя.</param>
    internal static string Classify(string? userAgent, bool hasWarmupHeader, IPAddress? remoteIp)
    {
        if (hasWarmupHeader && remoteIp is not null && IPAddress.IsLoopback(remoteIp)) return Warmup;
        if (string.IsNullOrEmpty(userAgent)) return Visitor;
        if (UptimeRx().IsMatch(userAgent)) return Uptime;
        if (BotRx().IsMatch(userAgent)) return Bot;
        return Visitor;
    }

    [GeneratedRegex("UptimeRobot|Pingdom|StatusCake|Better ?Uptime|AlwaysOn", RegexOptions.IgnoreCase)]
    private static partial Regex UptimeRx();

    // Общие слова (bot/crawl/spider) + известные роботы, у которых их нет в имени.
    [GeneratedRegex(@"bot\b|bot/|crawl|spider|slurp|facebookexternalhit|bingpreview|mediapartners|Google-InspectionTool|GoogleOther|Lighthouse|HeadlessChrome|python-requests|curl/|wget/|Go-http-client|okhttp|axios/|node-fetch|scrapy|ahrefs|semrush|mj12|petalbot|bytespider|gptbot|claudebot|ccbot|perplexity",
        RegexOptions.IgnoreCase)]
    private static partial Regex BotRx();
}
