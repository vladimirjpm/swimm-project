using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сводка фоновых служб (docs/admin-pages/jobs.md): одна и та же для плиток /Admin/Jobs и блока
/// «Jobs» дашборда — <see cref="JobsOverviewBuilder"/>. Статус расписания, просрочка, проблемы.
/// </summary>
public class JobsOverviewBuilderTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    private static JobRunStateRow State(DateTime? attempt, DateTime? success, string? error = null) =>
        new(JobNames.Discovery, attempt, success, error);

    private static readonly CacheBridgeState BridgeOk =
        new(true, "swimm_cache", true, Now.AddHours(-1), 0, 3, 0, 0, 5, null, null, null, null);

    private static JobsInfraState Infra(bool? dbOk = true, CacheBridgeState? bridge = null, DataSyncState? sync = null) =>
        new(AdminEnabled: true, IsDevelopment: false, Queued: 2, InProgress: 1, dbOk, Now.AddSeconds(-10), sync, bridge ?? BridgeOk);

    private static JobCard Card(string key, string kind, bool active = true, bool failed = false,
        DateTime? next = null, string? alert = null, string status = "ок") =>
        new(key, key, kind, key, "", new JobBadge("", "neutral"), [], [], null)
        { Active = active, Failed = failed, NextDueAt = next, Alert = alert, Status = status };

    private static readonly JobRunStats NoRuns = new(0, 0, 0, 0);

    // ── Статус расписания ──────────────────────────────────────────────────────

    [Fact]
    public void Schedule_ok_shows_last_success()
    {
        var s = JobsOverviewBuilder.Schedule(true, true, "DiscoveryEnabled", State(Now.AddHours(-3), Now.AddHours(-3)), Day, Now);
        Assert.True(s.Active);
        Assert.False(s.Failed);
        Assert.Null(s.Alert);
        Assert.Equal(Now.AddHours(21), s.NextDueAt);
        Assert.Equal("ок 3 ч назад", s.Status);
    }

    [Fact]
    public void Schedule_failure_is_danger_with_error_text()
    {
        var s = JobsOverviewBuilder.Schedule(true, true, "DiscoveryEnabled",
            State(Now.AddMinutes(-20), Now.AddDays(-2), "502 Bad Gateway"), Day, Now);
        Assert.True(s.Failed);
        Assert.Equal("danger", s.Alert);
        Assert.Equal("ошибка 20 мин назад: 502 Bad Gateway", s.Status);
    }

    [Fact]
    public void Schedule_overdue_is_warning()
    {
        // Успех 26 ч назад при интервале 24 ч: пора уже 2 ч, а прогона нет.
        var s = JobsOverviewBuilder.Schedule(true, true, "DiscoveryEnabled", State(Now.AddHours(-26), Now.AddHours(-26)), Day, Now);
        Assert.Equal("warning", s.Alert);
        Assert.StartsWith("просрочена на 2 ч", s.Status);
    }

    [Fact]
    public void Schedule_due_recently_is_not_overdue_yet()
    {
        // Пора 20 минут — фон ещё мог не дойти (смотрит журнал раз в 5–15 мин).
        var s = JobsOverviewBuilder.Schedule(true, true, "DiscoveryEnabled",
            State(Now.AddHours(-24).AddMinutes(-20), Now.AddHours(-24).AddMinutes(-20)), Day, Now);
        Assert.Null(s.Alert);
    }

    [Fact]
    public void Schedule_switched_off_is_never_a_problem()
    {
        var failedState = State(Now.AddHours(-30), Now.AddDays(-3), "таймаут");

        var byFlag = JobsOverviewBuilder.Schedule(false, true, "DiscoveryEnabled", failedState, Day, Now);
        Assert.False(byFlag.Active);
        Assert.Null(byFlag.Alert);
        Assert.StartsWith("выключена флагом процесса", byFlag.Status);

        var bySetting = JobsOverviewBuilder.Schedule(true, false, "DiscoveryEnabled", failedState, Day, Now);
        Assert.Null(bySetting.Alert);
        Assert.StartsWith("выключена настройкой (DiscoveryEnabled)", bySetting.Status);
        // Последний прогон всё равно виден.
        Assert.Contains("последний — ошибка", bySetting.Status);
    }

    [Fact]
    public void Schedule_never_ran()
    {
        var s = JobsOverviewBuilder.Schedule(true, true, "DiscoveryEnabled", null, Day, Now);
        Assert.Null(s.NextDueAt);
        Assert.Null(s.Alert);
        Assert.Equal("ещё не запускалась", s.Status);
    }

    // ── Сводка ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_counts_services_and_problems_worst_first()
    {
        JobCard[] cards =
        [
            Card("discovery", JobKinds.Schedule, next: Now.AddHours(-2), alert: "warning", status: "просрочена на 2 ч"),
            Card("start-list", JobKinds.Schedule, failed: true, next: Now.AddMinutes(40), alert: "danger", status: "ошибка"),
            Card("loglig-verify", JobKinds.Schedule, next: Now.AddHours(5)),
            Card("loglig-batch", JobKinds.Schedule, active: false),
            Card("import", JobKinds.Queue),
            Card("db-ping", JobKinds.Service),
        ];

        var s = JobsOverviewBuilder.Build(cards, new JobRunStats(3, 1, 10, 2), Infra(), Now);

        Assert.Equal(6, s.Total);
        Assert.Equal(5, s.Active);
        Assert.Equal(1, s.FailedSchedules);
        Assert.Equal(1, s.OverdueSchedules);
        Assert.Equal("danger", s.Tone);
        Assert.Equal(["start-list", "discovery"], s.Problems.Select(p => p.Key));
        // Ближайший — у start-list через 40 мин (discovery уже пора, он не «следующий»).
        Assert.Equal("start-list", s.NextTitle);

        var tiles = s.Tiles.ToDictionary(t => t.Key);
        Assert.Equal("5 / 6", tiles["services"].Value);
        Assert.Equal("danger", tiles["failed"].Tone);
        Assert.Equal("#job-start-list", tiles["failed"].Href);
        Assert.Equal("warning", tiles["overdue"].Tone);
        Assert.Equal("#job-discovery", tiles["overdue"].Href);
        Assert.Equal("через 40 мин", tiles["next"].Value);
        Assert.Equal("danger", tiles["runs24h"].Tone);
        Assert.Equal("2 · 1", tiles["queues"].Value);
        Assert.Equal("ok", tiles["db"].Tone);
    }

    [Fact]
    public void Build_all_quiet_is_ok()
    {
        JobCard[] cards = [Card("loglig-verify", JobKinds.Schedule, next: Now.AddHours(3)), Card("db-ping", JobKinds.Service)];

        var s = JobsOverviewBuilder.Build(cards, NoRuns, Infra(), Now);

        Assert.Equal("ok", s.Tone);
        Assert.Empty(s.Problems);
        Assert.Equal("neutral", s.Tiles.Single(t => t.Key == "services").Tone);
        Assert.Equal("ok", s.Tiles.Single(t => t.Key == "failed").Tone);
    }

    [Fact]
    public void Build_without_running_schedules_says_so()
    {
        // Локальная разработка: расписания выключены флагом процесса.
        JobCard[] cards = [Card("discovery", JobKinds.Schedule, active: false), Card("db-ping", JobKinds.Service)];

        var tiles = JobsOverviewBuilder.Build(cards, NoRuns, Infra(), Now).Tiles.ToDictionary(t => t.Key);

        Assert.Equal("—", tiles["overdue"].Value);
        Assert.Equal("neutral", tiles["overdue"].Tone);
        Assert.Equal("—", tiles["next"].Value);
        Assert.Equal("расписания здесь не работают", tiles["next"].Sub);
    }

    [Fact]
    public void Build_infra_tiles_follow_live_state()
    {
        var bridgeDown = BridgeOk with { Listening = false };
        var behind = new DataSyncState(80, "Results", null, 76, null, null, Now);
        JobCard[] cards = [Card("db-ping", JobKinds.Service, alert: "danger", status: "база НЕ отвечает")];

        var s = JobsOverviewBuilder.Build(cards, NoRuns, Infra(dbOk: false, bridge: bridgeDown, sync: behind), Now);
        var tiles = s.Tiles.ToDictionary(t => t.Key);

        Assert.Equal("не отвечает", tiles["db"].Value);
        Assert.Equal("danger", tiles["db"].Tone);
        Assert.Equal("не слушает", tiles["bridge"].Value);
        Assert.Equal("danger", tiles["bridge"].Tone);
        Assert.Equal("отстаёт", tiles["sync"].Value);
        Assert.Equal("warning", tiles["sync"].Tone);
        Assert.Equal("danger", s.Tone);
        Assert.Equal("db-ping", s.Problems.Single().Key);
    }

    [Fact]
    public void Time_text_is_human()
    {
        Assert.Equal("45 с", JobTimeText.Human(TimeSpan.FromSeconds(45)));
        Assert.Equal("20 мин", JobTimeText.Human(TimeSpan.FromMinutes(20)));
        Assert.Equal("3 ч 15 мин", JobTimeText.Human(TimeSpan.FromMinutes(195)));
        Assert.Equal("26 ч", JobTimeText.Human(TimeSpan.FromHours(26)));
        Assert.Equal("3 дн.", JobTimeText.Human(TimeSpan.FromDays(3)));
        Assert.Equal("только что", JobTimeText.Ago(Now.AddSeconds(5), Now));
        Assert.Equal("кнопка", JobTimeText.Trigger(JobTriggers.Manual));
    }
}
