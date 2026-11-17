using Azure.Monitor.OpenTelemetry.AspNetCore;
using Npgsql;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Swimm.API.Hosting;

namespace Swimm.API.Telemetry;

/// <summary>
/// Отправка телеметрии в Application Insights (docs/plans/application-insights-plan.md, А2).
/// Только сервер: браузерный SDK не ставим (куки, согласие, на сайте дети).
/// </summary>
public static class SwimmTelemetryExtensions
{
    internal const string AspNetCoreRedactionOffKey = "OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION";
    internal const string HttpClientRedactionOffKey = "OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION";

    public static WebApplicationBuilder AddSwimmTelemetry(this WebApplicationBuilder builder)
    {
        if (!HostingMode.TelemetryEnabled(builder.Configuration)) return builder;

        var roleName = TelemetryRoleName.Resolve(builder.Configuration, builder.Environment);
        var connectionString = builder.Configuration[HostingMode.TelemetryConnectionStringKey]!;

        // ⚠ ДО UseAzureMonitor: процессор вычистки должен стоять в цепочке раньше экспортёра.
        builder.Services.AddSwimmTelemetryGuards(builder.Configuration);

        builder.Services.AddOpenTelemetry()
            .UseAzureMonitor(o =>
            {
                o.ConnectionString = connectionString;
                // Выборку задаём явно (А2.6): умолчание пакета 1.6.0 — 5 трасс в секунду, и оно
                // менялось между версиями. Трафик мал, при 100 % видна каждая ошибка; страховка
                // от счёта — дневной лимит приёма на ресурсе (А1.3).
                o.SamplingRatio = 1.0F;
                o.TracesPerSecond = null;
            })
            // ПОСЛЕ UseAzureMonitor: детектор App Service ставит service.name = имя сайта,
            // а при слиянии ресурсов побеждает добавленный позже.
            .ConfigureResource(r => r.AddService(serviceName: roleName))
            // Postgres (А2.7): длительность и текст SQL с плейсхолдерами ($1), без значений параметров.
            .WithTracing(t => t.AddNpgsql());

        // В Azure — только Warning и выше (А2.5). Правило для провайдера важнее общих правил
        // Logging:LogLevel, поэтому Information категорий не просочится. Консоль — как была.
        builder.Logging.AddFilter<OpenTelemetryLoggerProvider>(null, LogLevel.Warning);

        return builder;
    }

    /// <summary>
    /// Фильтр запросов и вычистка адресов — без экспортёра, чтобы тест собрал тот же конвейер со
    /// своим сборщиком спанов (TelemetryRedactionTests).
    /// </summary>
    internal static IServiceCollection AddSwimmTelemetryGuards(this IServiceCollection services, IConfigurationBuilder configuration)
    {
        // UseAzureMonitor() выключает встроенную вычистку строки запроса (ставит эти ключи в True,
        // если они не заданы). Задаём false последним источником — его не перебьёт и переменная
        // окружения: выключать вычистку на этом сайте незачем.
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [AspNetCoreRedactionOffKey] = "false",
            [HttpClientRedactionOffKey] = "false",
        });

        services.ConfigureOpenTelemetryTracerProvider(t => t.AddProcessor(new TelemetryRedactionProcessor()));

        services.Configure<AspNetCoreTraceInstrumentationOptions>(o =>
        {
            o.Filter = ctx => TelemetryRequestFilter.ShouldRecord(ctx.Request.Path);
            // Своя вычистка поверх встроенной: правило строже (/auth — целиком, см. QueryStringRedactor).
            o.EnrichWithHttpRequest = (activity, _) => TelemetryRedactionProcessor.Redact(activity);
        });

        services.Configure<HttpClientTraceInstrumentationOptions>(o =>
        {
            o.EnrichWithHttpRequestMessage = (activity, _) => TelemetryRedactionProcessor.Redact(activity);
        });

        return services;
    }
}
