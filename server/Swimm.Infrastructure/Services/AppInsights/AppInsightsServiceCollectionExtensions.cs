using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Infrastructure.Services.AppInsights;

public static class AppInsightsServiceCollectionExtensions
{
    /// <summary>
    /// Чтение Application Insights для админки (план Б1). Вызывать только при включённой админке —
    /// на проде этого кода нет в контейнере DI. Сеть не трогает: клиент Azure создаётся лениво, при
    /// первом запросе страницы.
    /// </summary>
    /// <param name="process">Шлёт ли телеметрию этот процесс и под какой ролью — знает только API-слой.</param>
    public static IServiceCollection AddAppInsightsReader(
        this IServiceCollection services, IConfiguration configuration, AppInsightsProcessInfo process)
    {
        services.Configure<AppInsightsOptions>(configuration.GetSection(AppInsightsOptions.SectionName));
        services.AddSingleton(process);
        services.AddSingleton<IAppInsightsQueryRunner, AzureLogsQueryRunner>();
        services.AddSingleton<IAppInsightsReader, AppInsightsReader>();
        return services;
    }
}
