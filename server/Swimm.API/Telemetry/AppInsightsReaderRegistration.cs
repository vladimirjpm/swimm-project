using Swimm.API.Hosting;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Services.AppInsights;

namespace Swimm.API.Telemetry;

/// <summary>
/// Чтение телеметрии для админки (/Admin/AppInsights, блок дашборда — план Б1). Вызывается только
/// при включённой админке: на проде (В17) читателя в контейнере нет, а путь /api/admin отдаёт 404.
/// </summary>
internal static class AppInsightsReaderRegistration
{
    public static WebApplicationBuilder AddAppInsightsAdminReader(this WebApplicationBuilder builder)
    {
        // Шлёт ли сам этот процесс телеметрию — для шапки страницы; знает об этом только API-слой.
        var process = new AppInsightsProcessInfo(
            HostingMode.TelemetryEnabled(builder.Configuration),
            TelemetryRoleName.Resolve(builder.Configuration, builder.Environment));
        builder.Services.AddAppInsightsReader(builder.Configuration, process);
        return builder;
    }
}
