using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using Swimm.API.Hosting;
using Swimm.API.Telemetry;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Настоящая сборка <see cref="SwimmTelemetryExtensions.AddSwimmTelemetry"/> вместе с
/// <c>UseAzureMonitor()</c> — то, что в тексте плана не проверить: пакет не перебивает имя роли
/// и не выключает обратно вычистку строки запроса. Наружу ничего не уходит: адрес приёма — закрытый
/// локальный порт, Live Metrics, офлайн-хранилище и Statsbeat выключены, хост не стартует.
/// </summary>
[Collection(TelemetryCollection.Name)]
public class TelemetryWiringTests
{
    private const string FakeConnectionString =
        "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://127.0.0.1:9/;LiveEndpoint=http://127.0.0.1:9/";

    private static WebApplication Build(string environment, string? connectionString = FakeConnectionString)
    {
        Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_STATSBEAT_DISABLED", "true");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [HostingMode.TelemetryConnectionStringKey] = connectionString,
        });
        builder.AddSwimmTelemetry();
        builder.Services.PostConfigure<AzureMonitorOptions>(o =>
        {
            o.EnableLiveMetrics = false;
            o.DisableOfflineStorage = true;
        });
        return builder.Build();
    }

    [Theory]
    [InlineData("Production", "swimhub-web")]
    [InlineData(HostingMode.ProdAdminEnvironment, "swimhub-prodadmin")]
    public async Task RoleName_SurvivesAzureMonitorResourceDetectors(string environment, string expected)
    {
        // Как на App Service: детектор пакета ставит service.name = имя сайта. Наше должно победить.
        Environment.SetEnvironmentVariable("WEBSITE_SITE_NAME", "swimhub");
        try
        {
            await using var app = Build(environment);
            var attrs = app.Services.GetRequiredService<TracerProvider>().GetResource().Attributes
                .ToDictionary(a => a.Key, a => a.Value);

            Assert.Equal("azure_app_service", attrs["cloud.platform"]);   // детектор и правда отработал
            Assert.Equal(expected, attrs["service.name"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WEBSITE_SITE_NAME", null);
        }
    }

    [Fact]
    public async Task BuiltInQueryRedaction_StaysOn_DespiteAzureMonitor()
    {
        await using var app = Build("Production");
        // Опции Azure Monitor материализуются — в этот момент пакет ставит свои ключи, если их нет.
        _ = app.Services.GetRequiredService<IOptions<AzureMonitorOptions>>().Value;

        Assert.Equal("false", app.Configuration[SwimmTelemetryExtensions.AspNetCoreRedactionOffKey]);
        Assert.Equal("false", app.Configuration[SwimmTelemetryExtensions.HttpClientRedactionOffKey]);
    }

    [Fact]
    public async Task Sampling_IsExplicit_AllTraces()
    {
        await using var app = Build("Production");
        var o = app.Services.GetRequiredService<IOptions<AzureMonitorOptions>>().Value;
        Assert.Equal(1.0F, o.SamplingRatio);
        Assert.Null(o.TracesPerSecond);
    }

    [Fact]
    public async Task Logs_ToAzure_WarningAndAbove()
    {
        await using var app = Build("Production");
        var rules = app.Services.GetRequiredService<IOptions<LoggerFilterOptions>>().Value.Rules;
        var rule = Assert.Single(rules, r => r.ProviderName == typeof(OpenTelemetryLoggerProvider).FullName);
        Assert.Null(rule.CategoryName);
        Assert.Equal(LogLevel.Warning, rule.LogLevel);
    }

    [Fact]
    public async Task WithoutConnectionString_NoOpenTelemetryAtAll()
    {
        await using var app = Build("Production", connectionString: null);
        Assert.Null(app.Services.GetService<TracerProvider>());
    }
}
