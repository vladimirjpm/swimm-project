using Microsoft.AspNetCore.Http;
using Swimm.API.Telemetry;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Health-check App Service (раз в минуту) и статика не пишутся в телеметрию — иначе они съедят
/// дневной лимит приёма (docs/plans/application-insights-plan.md, А2.4).
/// </summary>
[Collection(TelemetryCollection.Name)]
public class TelemetryFilterTests
{
    [Theory]
    [InlineData("/healthz")]
    [InlineData("/readyz")]
    [InlineData("/version")]
    [InlineData("/HEALTHZ")]
    [InlineData("/assets/index-abc123.js")]
    [InlineData("/images/logo.png")]
    [InlineData("/css/admin.min.css")]
    [InlineData("/js/admin/jobs.js")]
    [InlineData("/favicon.ico")]
    [InlineData("/sw.js")]
    [InlineData("/manifest.json")]
    [InlineData("/robots.txt")]
    public void Skipped(string path) => Assert.False(TelemetryRequestFilter.ShouldRecord(new PathString(path)));

    [Theory]
    [InlineData("/")]
    [InlineData("/competitions/1581")]
    [InlineData("/results_main.html")]
    [InlineData("/api/results")]
    [InlineData("/api/records/export.csv")]   // у API «расширение» не делает его статикой
    [InlineData("/auth/reset-password")]
    [InlineData("/Admin/Jobs")]
    [InlineData("/healthz-extra")]
    public void Recorded(string path) => Assert.True(TelemetryRequestFilter.ShouldRecord(new PathString(path)));

    [Fact]
    public async Task Pipeline_HealthAndStatic_NotExported()
    {
        await using var pipeline = await TelemetryTestPipeline.StartAsync();

        await pipeline.GetAsync("/healthz");
        await pipeline.GetAsync("/assets/index.js");
        await pipeline.GetAsync("/favicon.ico");
        await pipeline.GetAsync("/competitions/1581");

        var span = Assert.Single(pipeline.Spans);
        Assert.Equal("/competitions/1581", span.Tag("url.path"));
    }
}

/// <summary>
/// Слушатель Activity у OpenTelemetry глобальный: два конвейера, запущенные параллельно, видели бы
/// спаны друг друга. Тесты с живым конвейером идут одной коллекцией — последовательно.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetryCollection
{
    public const string Name = "Telemetry pipeline";
}
