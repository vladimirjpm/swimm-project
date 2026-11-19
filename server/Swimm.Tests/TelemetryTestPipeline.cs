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

namespace Swimm.Tests;

/// <summary>
/// Живой конвейер ASP.NET Core + OpenTelemetry с теми же фильтром и вычисткой, что на проде
/// (<see cref="SwimmTelemetryExtensions.AddSwimmTelemetryGuards"/>), но вместо экспортёра Azure —
/// сборщик, который СНИМАЕТ КОПИЮ тегов в момент экспорта: если вычистка опоздает и случится
/// после экспортёра, тест это увидит (ссылка на Activity такую ошибку спрятала бы).
/// Сеть не нужна: TestServer.
/// </summary>
internal sealed class TelemetryTestPipeline : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly SnapshotExporter _exporter = new();

    private TelemetryTestPipeline(WebApplication app, SnapshotExporter exporter)
    {
        _app = app;
        _exporter = exporter;
    }

    public IReadOnlyList<SpanSnapshot> Spans => _exporter.Spans;

    /// <param name="builtInRedactionOff">
    /// true — выключить встроенную вычистку инструментария ПОСЛЕ наших настроек, как это делает
    /// <c>UseAzureMonitor()</c>, если ему не мешать. Проверяет, что наша вычистка держит одна.
    /// </param>
    public static async Task<TelemetryTestPipeline> StartAsync(bool builtInRedactionOff = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();

        builder.Services.AddSwimmTelemetryGuards(builder.Configuration);
        if (builtInRedactionOff)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SwimmTelemetryExtensions.AspNetCoreRedactionOffKey] = "true",
                [SwimmTelemetryExtensions.HttpClientRedactionOffKey] = "true",
            });
        }

        var exporter = new SnapshotExporter();
        builder.Services.AddOpenTelemetry().WithTracing(t => t
            .AddAspNetCoreInstrumentation()
            .AddProcessor(new SimpleActivityExportProcessor(exporter)));

        var app = builder.Build();
        RequestDelegate ok = ctx => ctx.Response.WriteAsync("ok");
        app.Map("/{**path}", ok);

        await app.StartAsync();
        return new TelemetryTestPipeline(app, exporter);
    }

    /// <summary>Шлёт GET и ждёт, пока спан запроса закроется (он закрывается чуть позже ответа).</summary>
    public async Task GetAsync(string pathAndQuery, IReadOnlyDictionary<string, string>? headers = null)
    {
        var before = _exporter.Spans.Count;
        using var client = _app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
            request.Headers.TryAddWithoutValidation(name, value);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        // Отфильтрованный запрос спана не даёт вовсе — ждём недолго и не падаем.
        for (var i = 0; i < 20 && _exporter.Spans.Count == before; i++)
            await Task.Delay(25);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    internal sealed record SpanSnapshot(string DisplayName, IReadOnlyDictionary<string, string?> Tags)
    {
        public string? Tag(string key) => Tags.TryGetValue(key, out var v) ? v : null;

        /// <summary>Всё, что могло бы уехать наружу текстом.</summary>
        public string AllText => DisplayName + "\n" + string.Join("\n", Tags.Select(t => $"{t.Key}={t.Value}"));
    }

    private sealed class SnapshotExporter : BaseExporter<Activity>
    {
        private readonly List<SpanSnapshot> _spans = new();

        public IReadOnlyList<SpanSnapshot> Spans
        {
            get { lock (_spans) return _spans.ToList(); }
        }

        public override ExportResult Export(in Batch<Activity> batch)
        {
            lock (_spans)
            {
                foreach (var a in batch)
                {
                    var tags = a.TagObjects.ToDictionary(t => t.Key, t => t.Value?.ToString());
                    _spans.Add(new SpanSnapshot(a.DisplayName, tags));
                }
            }
            return ExportResult.Success;
        }
    }
}
