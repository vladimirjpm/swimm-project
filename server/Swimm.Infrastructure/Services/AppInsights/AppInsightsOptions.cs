using Swimm.Application.Mapping;

namespace Swimm.Infrastructure.Services.AppInsights;

/// <summary>
/// Раздел <c>AppInsights</c> конфигурации (план §4.1). <see cref="WorkspaceId"/> и <see cref="ResourceId"/>
/// — ТОЛЬКО в gitignored <c>appsettings.Local.json</c> / <c>appsettings.ProdAdmin.Local.json</c>: репо
/// публичный, идентификаторы Azure в него не попадают. В <c>*.example</c> — только имена ключей.
/// </summary>
public sealed class AppInsightsOptions
{
    public const string SectionName = "AppInsights";
    public const string WorkspaceIdKey = SectionName + ":WorkspaceId";
    public const string ResourceIdKey = SectionName + ":ResourceId";

    /// <summary>customerId рабочей области Log Analytics <c>swimhub-logs</c> (GUID) — по нему идут запросы.</summary>
    public string? WorkspaceId { get; set; }

    /// <summary>Полный id ресурса <c>swimhub-ai</c> (<c>/subscriptions/…/components/swimhub-ai</c>) — для ссылок в портал.</summary>
    public string? ResourceId { get; set; }

    /// <summary>Дневной лимит приёма рабочей области, ГБ (А1.3). Не секрет — умолчание 0,15.</summary>
    public double DailyCapGb { get; set; } = AppInsightsThresholds.DefaultDailyCapGb;

    /// <summary>Час сброса дневного лимита, UTC (у <c>swimhub-logs</c> — 10).</summary>
    public int CapResetHourUtc { get; set; } = AppInsightsThresholds.DefaultCapResetHourUtc;

    /// <summary>Лимит в МБ, как <c>Usage.Quantity</c>. Azure считает ГБ десятичными — 0,15 ГБ = 150 МБ (так же порог 120 МБ у письма «Объём»).</summary>
    public double DailyCapMb => DailyCapGb * 1000;

    /// <summary>Каких ключей не хватает — для карточки «не настроено».</summary>
    public IReadOnlyList<string> MissingKeys()
    {
        var missing = new List<string>(2);
        if (string.IsNullOrWhiteSpace(WorkspaceId)) missing.Add(WorkspaceIdKey);
        if (string.IsNullOrWhiteSpace(ResourceId)) missing.Add(ResourceIdKey);
        return missing;
    }
}
