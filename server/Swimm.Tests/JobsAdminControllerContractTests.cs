using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Swimm.API.Controllers;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Контракт GET /api/admin/jobs/monitor и /runs/{id} (docs/admin-pages/jobs.md «Контракт
/// мониторинга»): имена полей JSON — то, на что опираются страница /Admin/Jobs и блок дашборда.
/// Переименование поля в DTO должно ронять этот тест, а не клиент на проде. Сериализация — как у
/// контроллеров (<see cref="JsonSerializerDefaults.Web"/>: camelCase).
/// </summary>
public class JobsAdminControllerContractTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 14, 20, 0, DateTimeKind.Utc);

    private sealed class StatusStub(JobsMonitor monitor, JobRunDetailsView? run) : IBackgroundJobsStatus
    {
        public Task<BackgroundJobsReport> GetReportAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<JobsSummary> GetSummaryAsync(CancellationToken ct = default) => Task.FromResult(monitor.Summary);
        public Task<JobsMonitor> GetMonitorAsync(CancellationToken ct = default) => Task.FromResult(monitor);
        public Task<JobRunDetailsView?> GetRunDetailsAsync(long id, CancellationToken ct = default) =>
            Task.FromResult(run?.Id == id ? run : null);
    }

    private static JobsMonitor Monitor()
    {
        var bridge = new CacheBridgeState(true, "swimm_cache", true, Now.AddHours(-1), 0, 3, 0, 0, 5, null, null, null, null);
        var infra = new JobsInfraState(true, false, 0, 0, true, Now, null, bridge);
        JobCard[] cards =
        [
            new(JobNames.Discovery, "Входящие isr.org.il", JobKinds.Schedule, "svc", "что", new JobBadge("работает", "success"), [], [], null)
            {
                Active = true, Status = "ок 1 ч назад",
                Monitor = new JobMonitorExtras
                {
                    Zone = JobZones.Prod, TimelineMode = JobTimelineModes.Runs, Interval = TimeSpan.FromHours(24),
                    NextAt = Now.AddHours(17), Claim = new JobClaimInfo(Now.AddHours(-3), "Production@a#1", false, JobOrigins.Prod),
                    Toggle = new JobToggle("DiscoveryEnabled", true, false),
                    Actions = [new(JobAction.Link, "Входящие", "/Admin/Competitions")],
                },
            },
        ];
        var summary = JobsOverviewBuilder.Build(cards, new JobRunStats(1, 0, 1, 0), infra, Now, waiting: 1);
        return JobsMonitorBuilder.Build(new JobsMonitorInput(Now, "ProdAdmin", "prod", cards,
            [new JobRunLogRow(42, JobNames.Discovery, JobTriggers.Schedule, Now.AddHours(-1), Now.AddHours(-1).AddSeconds(30), true,
                "на сайте 81", null, null, "Production@a#1") { HasDetails = true }],
            summary,
            [new ResultsAutoWaitingRow(7, 16925, "ליגה", Now, Now, "бассейн не задан", Now.AddHours(-2))],
            "ProdAdmin@VLADDELL#6392", Now.AddHours(-5), Now.AddMinutes(-4),
            new RecordsCheckTile("2 ч назад", "изменений нет", "ok", Now.AddHours(-2)), []));
    }

    private static JsonElement Json(IActionResult result) =>
        JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static void HasProps(JsonElement e, params string[] names)
    {
        foreach (var n in names)
            Assert.True(e.TryGetProperty(n, out _), $"нет поля «{n}»");
    }

    [Fact]
    public async Task Monitor_json_contract()
    {
        var controller = new JobsAdminController(new StatusStub(Monitor(), null));
        var json = Json(await controller.Monitor(CancellationToken.None));

        HasProps(json, "now", "environment", "journal", "windowFrom", "windowTo", "windowHours", "zones", "counters",
            "jobs", "attention", "running", "upcoming", "waiting", "summary", "recordsSources", "recentImports");
        HasProps(json.GetProperty("counters"), "failed", "overdue", "running", "waiting", "ok", "off");
        Assert.Equal(1, json.GetProperty("counters").GetProperty("waiting").GetInt32());
        // Сводка — та же, что /summary, и в ней те же счётчики.
        HasProps(json.GetProperty("summary"), "total", "active", "problems", "tone", "tiles", "counters");

        var zone = json.GetProperty("zones")[0];
        HasProps(zone, "key", "process", "pulseAt", "pulseKind", "jobKeys");
        Assert.Equal("prod", zone.GetProperty("key").GetString());

        var job = json.GetProperty("jobs")[0];
        HasProps(job, "key", "title", "kind", "zone", "status", "badge", "statusText", "alert", "active", "what", "service",
            "intervalSeconds", "intervalKey", "toggle", "settings", "lastRunAt", "lastSuccessAt", "lastRun", "lastDurationSeconds",
            "nextAt", "nextIsRetry", "nextApprox", "dueNow", "overdueSince", "overdueSeconds", "claim", "lastError", "lastSummary",
            "stateSince", "recentOutcomes", "timelineMode", "windowRuns", "planned", "progress", "queue", "facts", "actions", "history");
        Assert.Equal("ok", job.GetProperty("status").GetString());
        Assert.Equal(86400, job.GetProperty("intervalSeconds").GetInt32());
        HasProps(job.GetProperty("claim"), "since", "by", "live", "origin");
        HasProps(job.GetProperty("toggle"), "key", "value", "isDefault");
        HasProps(job.GetProperty("actions")[0], "kind", "text", "url", "hint");
        HasProps(job.GetProperty("windowRuns")[0], "id", "startedAt", "finishedAt", "outcome", "trigger", "origin", "runBy");

        var row = job.GetProperty("history")[0];
        HasProps(row, "id", "startedAt", "finishedAt", "durationSeconds", "trigger", "triggerText", "origin", "originText",
            "runBy", "success", "summary", "error", "hasDetails");
        Assert.True(row.GetProperty("hasDetails").GetBoolean());
        // Подробности в основном ответе не ездят — только признак.
        Assert.False(row.TryGetProperty("details", out _));

        var attention = json.GetProperty("attention");
        HasProps(attention[0], "key", "title", "zone", "kind", "status", "text", "since", "href");
        Assert.Contains(attention.EnumerateArray(), a => a.GetProperty("kind").GetString() == "abandoned-claim");
        Assert.Contains(attention.EnumerateArray(), a => a.GetProperty("kind").GetString() == "waiting");
    }

    [Fact]
    public async Task Run_details_found_and_missing()
    {
        using var doc = JsonDocument.Parse("""{"changes":[{"kind":"new"}]}""");
        var run = new JobRunDetailsView(42, JobNames.Discovery, JobTriggers.Schedule, Now, Now.AddSeconds(30), true,
            "Production@a#1", JobOrigins.Prod, "на сайте 81", null, doc.RootElement.Clone(), null);
        var controller = new JobsAdminController(new StatusStub(Monitor(), run));

        var json = Json(await controller.Run(42, CancellationToken.None));
        HasProps(json, "id", "name", "trigger", "startedAt", "finishedAt", "success", "runBy", "origin", "summary", "error",
            "details", "discoveryChanges");
        Assert.Equal("new", json.GetProperty("details").GetProperty("changes")[0].GetProperty("kind").GetString());

        Assert.IsType<NotFoundResult>(await controller.Run(43, CancellationToken.None));
    }
}
