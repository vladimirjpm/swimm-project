using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Swimm.API.Telemetry;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// SQL в телеметрии (решение Влада 19.11.2026): быстрые команды (&lt; 400 мс, не упавшие) в Azure не
/// уходят, медленные и упавшие — уходят; на спане запроса — счёт ВСЕХ команд и их суммарное время.
/// Живой конвейер ASP.NET Core + те же процессоры, что на проде (AddSwimmTelemetryGuards), и ПАКЕТНЫЙ
/// процессор экспорта — как у экспортёра Azure Monitor. SQL-спаны — из источника «Npgsql» с заданной
/// длительностью, база не нужна.
/// </summary>
[Collection(TelemetryCollection.Name)]
public class SqlTelemetryTests : IAsyncLifetime
{
    private static readonly ActivitySource Npgsql = new("Npgsql");

    private WebApplication _app = null!;
    private BatchActivityExportProcessor _export = null!;
    private readonly List<Activity> _exported = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Services.AddSwimmTelemetryGuards(builder.Configuration);

        _export = new BatchActivityExportProcessor(new ListExporter(_exported));
        builder.Services.AddOpenTelemetry().WithTracing(t => t
            .AddAspNetCoreInstrumentation()
            .AddSource(SqlSpans.NpgsqlSourceName)
            .AddProcessor(_export));

        _app = builder.Build();
        _app.MapGet("/cached", () => "ok");
        _app.MapGet("/mixed", () =>
        {
            Sql("SELECT fast 1", 5);
            Sql("SELECT fast 2", 120);
            Sql("SELECT fast 3", 399);
            Sql("SELECT slow", 450);
            Sql("SELECT failed", 3, failed: true);
            return "ok";
        });
        _app.MapGet("/parallel", async () =>
        {
            await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => Sql($"SELECT p{i}", 10))));
            return "ok";
        });
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _export.Dispose();
    }

    /// <summary>SQL-спан с заданной длительностью, как его закрыл бы Npgsql.</summary>
    internal static void Sql(string text, int ms, bool failed = false)
    {
        using var a = Npgsql.StartActivity(text, ActivityKind.Client);
        if (a is null) return;
        if (failed) a.SetStatus(ActivityStatusCode.Error, "boom");
        a.SetEndTime(a.StartTimeUtc + TimeSpan.FromMilliseconds(ms));
    }

    private async Task<List<Activity>> GetAndFlush(string path)
    {
        using var client = _app.GetTestClient();
        using var r = await client.GetAsync(path);
        r.EnsureSuccessStatusCode();
        // Спан запроса закрывается чуть позже ответа.
        for (var i = 0; i < 40 && !_exported.Any(a => a.Kind == ActivityKind.Server); i++)
        {
            _export.ForceFlush();
            await Task.Delay(25);
        }
        _export.ForceFlush();
        lock (_exported) return _exported.ToList();
    }

    private static Activity RequestSpan(List<Activity> spans) => Assert.Single(spans, a => a.Kind == ActivityKind.Server);
    internal static List<string> SqlSpans_(List<Activity> spans) =>
        spans.Where(a => a.Source.Name == SqlSpans.NpgsqlSourceName).Select(a => a.DisplayName).OrderBy(x => x).ToList();

    [Fact]
    public async Task FastSqlDropped_SlowAndFailedKept()
    {
        var spans = await GetAndFlush("/mixed");
        Assert.Equal(["SELECT failed", "SELECT slow"], SqlSpans_(spans));
    }

    [Fact]
    public async Task RequestSpan_CountsAllSql_IncludingDropped()
    {
        var spans = await GetAndFlush("/mixed");
        var req = RequestSpan(spans);
        Assert.Equal(5, req.GetTagItem(SqlSpans.QueryCountTag));
        Assert.Equal(977.0, (double)req.GetTagItem(SqlSpans.QueryMsTag)!, precision: 0);
    }

    [Fact]
    public async Task CachedRequest_ZeroSql()
    {
        var spans = await GetAndFlush("/cached");
        var req = RequestSpan(spans);
        Assert.Equal(0, req.GetTagItem(SqlSpans.QueryCountTag));
        Assert.Empty(SqlSpans_(spans));
    }

    [Fact]
    public async Task ParallelSql_AllCounted()
    {
        var spans = await GetAndFlush("/parallel");
        Assert.Equal(20, RequestSpan(spans).GetTagItem(SqlSpans.QueryCountTag));
        Assert.Empty(SqlSpans_(spans));
    }

    [Fact]
    public void BackgroundSql_NoRequest_FilteredByDurationOnly()
    {
        // Вне запроса (фоновые службы): счётчику некуда писать, фильтр работает как обычно.
        Sql("SELECT 1", 2);
        Sql("SELECT bg slow", 800);
        _export.ForceFlush();
        List<Activity> spans;
        lock (_exported) spans = _exported.ToList();
        Assert.Equal(["SELECT bg slow"], SqlSpans_(spans));
    }

    internal sealed class ListExporter(List<Activity> sink) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            lock (sink) foreach (var a in batch) sink.Add(a);
            return ExportResult.Success;
        }
    }
}

/// <summary>
/// Порог из конфига — отдельным классом: два конвейера сразу слушали бы один источник «Npgsql», и
/// фильтр чужого (порог 400) снимал бы флаг раньше нашего.
/// </summary>
[Collection(TelemetryCollection.Name)]
public class SqlTelemetryThresholdTests
{
    [Fact]
    public async Task Threshold_ComesFromConfig()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [SqlSpans.MinDurationMsKey] = "50" });
        var exported = new List<Activity>();
        using var export = new BatchActivityExportProcessor(new SqlTelemetryTests.ListExporter(exported));
        builder.Services.AddSwimmTelemetryGuards(builder.Configuration);
        builder.Services.AddOpenTelemetry().WithTracing(t => t.AddSource(SqlSpans.NpgsqlSourceName).AddProcessor(export));
        await using var app = builder.Build();
        await app.StartAsync();

        SqlTelemetryTests.Sql("SELECT 40ms", 40);
        SqlTelemetryTests.Sql("SELECT 60ms", 60);
        export.ForceFlush();

        lock (exported) Assert.Equal(["SELECT 60ms"], SqlTelemetryTests.SqlSpans_(exported.ToList()));
        await app.StopAsync();
    }
}
