using Swimm.Application.Dtos;

namespace Swimm.Infrastructure.Services.AppInsights;

/// <summary>
/// Ссылки в портал Azure на ресурс <c>swimhub-ai</c> — собираются во время работы из
/// <c>AppInsights:ResourceId</c> (план Б2 п. 1): в разметке и в репо идентификаторов нет.
/// </summary>
internal static class AppInsightsPortalLinks
{
    private const string Portal = "https://portal.azure.com/#@/resource";

    /// <summary>null — id не похож на id ресурса Azure (<c>/subscriptions/…</c>): ссылок не даём.</summary>
    public static AppInsightsLinks? Build(string? resourceId)
    {
        var id = resourceId?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(id)
            || !id.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase)
            || id.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '<' or '>' or '#' or '?'))
            return null;

        var root = Portal + id;
        return new AppInsightsLinks(
            Overview: root + "/overview",
            Failures: root + "/failures",
            Performance: root + "/performance",
            LiveMetrics: root + "/quickPulse",
            Logs: root + "/logs");
    }
}
