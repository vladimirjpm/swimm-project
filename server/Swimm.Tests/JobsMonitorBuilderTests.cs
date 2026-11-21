using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Мониторинг /Admin/Jobs (GET /api/admin/jobs/monitor, docs/admin-pages/jobs.md «Контракт
/// мониторинга»): статус из шести по бейджу, зона, окно 36 ч, план, исходы, счётчики, панели
/// «Требует внимания» / «Идёт сейчас» / «Ближайшие запуски» — <see cref="JobsMonitorBuilder"/>.
/// </summary>
public class JobsMonitorBuilderTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 14, 20, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    private static JobCard Card(string key, string kind, string variant, string badge = "", bool active = true,
        JobMonitorExtras? extras = null, string status = "ок") =>
        new(key, key, kind, key, "что делает", new JobBadge(badge, variant), [new JobFact("Факт", "значение")], [], null)
        {
            Active = active, Status = status, Monitor = extras ?? new JobMonitorExtras(),
        };

    private static JobRunLogRow Run(long id, string name, DateTime started, bool success = true, string? runBy = null, bool details = false) =>
        new(id, name, JobTriggers.Schedule, started, started.AddMinutes(2), success,
            success ? $"итог {id}" : null, success ? null : $"ошибка {id}", null, runBy) { HasDetails = details };

    // ── Статус и зона ──────────────────────────────────────────────────────────

    /// <summary>Таблица «Статусы» передачи дизайнера: бейдж → один из шести.</summary>
    [Theory]
    [InlineData(JobKinds.Schedule, "danger", JobStatuses.Failed)]    // «последний запуск упал», «последний тик упал»
    [InlineData(JobKinds.Service, "danger", JobStatuses.Failed)]     // «база недоступна», «не слушает»
    [InlineData(JobKinds.Schedule, "warning", JobStatuses.Overdue)]  // «просрочена», «молчит», «ещё не запускалась»
    [InlineData(JobKinds.Service, "warning", JobStatuses.Overdue)]   // «локальная отстаёт», «прод недоступен», «ждёт первого пинга»
    [InlineData(JobKinds.Schedule, "info", JobStatuses.Running)]     // «идёт сейчас»
    [InlineData(JobKinds.Queue, "info", JobStatuses.Running)]        // «импортирует», «идёт прогон», «идёт проверка»
    [InlineData(JobKinds.Schedule, "success", JobStatuses.Ok)]       // «работает»
    [InlineData(JobKinds.Service, "success", JobStatuses.Ok)]        // «база доступна», «слушает», «в синхроне»
    [InlineData(JobKinds.Queue, "success", JobStatuses.Idle)]        // «ждёт задания»
    [InlineData(JobKinds.Schedule, "neutral", JobStatuses.Off)]      // «выключена», «не в этом процессе»
    [InlineData(JobKinds.Service, "neutral", JobStatuses.Off)]       // «выключен»
    public void Status_from_badge(string kind, string variant, string expected) =>
        Assert.Equal(expected, JobsMonitorBuilder.Status(kind, variant));

    [Theory]
    [InlineData(false, true, false, JobZones.Prod)]   // ProdAdmin следит за прод-журналом
    [InlineData(true, false, false, JobZones.Local)]  // запущена здесь
    [InlineData(false, false, false, JobZones.Local)] // выключена здесь и не наблюдается
    [InlineData(false, true, true, JobZones.Local)]   // Development: зоны прод нет
    [InlineData(true, true, false, JobZones.Local)]   // запущена здесь — своя, даже если «наблюдение» тоже
    public void Zone_rule(bool processOn, bool watching, bool isDevelopment, string expected) =>
        Assert.Equal(expected, JobsMonitorBuilder.Zone(processOn, watching, isDevelopment));

    [Theory]
    [InlineData("Production@ca89e34f3d73#1901", JobOrigins.Prod)]
    [InlineData("ProdAdmin@VLADDELL#6392", JobOrigins.ProdAdmin)]
    [InlineData("Development@VLADDELL#12", JobOrigins.Local)]
    [InlineData("Staging@x#1", JobOrigins.Other)]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    public void Origin_code(string? runBy, string? expected) => Assert.Equal(expected, JobsMonitorBuilder.OriginCode(runBy));

    // ── Окно и план ────────────────────────────────────────────────────────────

    [Fact]
    public void Window_is_36h_with_now_in_the_middle_and_hour_aligned()
    {
        var (from, to) = JobsMonitorBuilder.Window(Now);
        Assert.Equal(new DateTime(2026, 11, 19, 20, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(TimeSpan.FromHours(36), to - from);
        Assert.True(Now - from >= TimeSpan.FromHours(18) && to - Now >= TimeSpan.FromHours(17));
    }

    [Fact]
    public void Planned_steps_from_next_due_to_window_end()
    {
        var (_, to) = JobsMonitorBuilder.Window(Now); // до 21.11 08:00
        var planned = JobsMonitorBuilder.Planned(Now.AddHours(2), TimeSpan.FromHours(6), Now, to);
        Assert.Equal([Now.AddHours(2), Now.AddHours(8), Now.AddHours(14)], planned);
    }

    [Fact]
    public void Planned_when_due_already_passed_starts_one_step_from_now()
    {
        var (_, to) = JobsMonitorBuilder.Window(Now);
        var planned = JobsMonitorBuilder.Planned(Now.AddHours(-1), Day, Now, to);
        Assert.Empty(planned); // сутки от «сейчас» — за краем окна (до конца 17 ч 40 мин)

        var hourly = JobsMonitorBuilder.Planned(null, TimeSpan.FromHours(1), Now, to);
        Assert.Equal(Now.AddHours(1), hourly[0]);
        Assert.Equal(17, hourly.Count);
        Assert.All(hourly, t => Assert.True(t > Now && t <= to));
    }

    [Fact]
    public void Planned_is_capped() =>
        Assert.Equal(JobsMonitorBuilder.MaxPlanned, JobsMonitorBuilder.Planned(null, TimeSpan.FromMinutes(5), Now, Now.AddHours(18)).Count);

    [Fact]
    public void Outcomes_newest_first_running_on_top_capped_at_15()
    {
        var history = Enumerable.Range(0, 20).Select(i => Run(i, "x", Now.AddHours(-i), success: i != 1)).ToList();
        var outcomes = JobsMonitorBuilder.Outcomes(history, running: true);
        Assert.Equal(15, outcomes.Count);
        Assert.Equal([JobRunOutcomes.Running, JobRunOutcomes.Success, JobRunOutcomes.Failed], outcomes.Take(3));
    }

    // ── Служба целиком ─────────────────────────────────────────────────────────

    [Fact]
    public void Item_schedule_window_runs_history_and_plan()
    {
        var (from, to) = JobsMonitorBuilder.Window(Now);
        var card = Card(JobNames.ResultsAuto, JobKinds.Schedule, "success", "работает", extras: new JobMonitorExtras
        {
            Zone = JobZones.Prod,
            TimelineMode = JobTimelineModes.Runs,
            Interval = TimeSpan.FromHours(1),
            NextAt = Now.AddMinutes(20),
            LastRunAt = Now.AddMinutes(-40),
        });
        var log = new[]
        {
            Run(3, JobNames.ResultsAuto, Now.AddMinutes(-40), runBy: "Production@a#1", details: true),
            Run(2, JobNames.ResultsAuto, Now.AddHours(-10), success: false),
            Run(1, JobNames.ResultsAuto, Now.AddHours(-30)), // за окном
        };

        var item = JobsMonitorBuilder.Item(card, log, Now, from, to);

        Assert.Equal(JobStatuses.Ok, item.Status);
        Assert.Equal(JobZones.Prod, item.Zone);
        Assert.Equal(3600, item.IntervalSeconds);
        Assert.Equal([2L, 3L], item.WindowRuns.Select(r => r.Id!.Value)); // по возрастанию, без старой
        Assert.Equal(JobOrigins.Prod, item.WindowRuns[1].Origin);
        Assert.Equal(3, item.History.Count);
        Assert.True(item.History[0].HasDetails);
        Assert.Equal("прод", item.History[0].OriginText);
        Assert.Equal("фон", item.History[0].TriggerText);
        Assert.Equal(120, item.LastDurationSeconds);
        Assert.Equal("итог 3", item.LastSummary);
        Assert.Equal(Now.AddMinutes(20), item.Planned[0]);
        Assert.False(item.DueNow);
        Assert.Null(item.OverdueSince);
        Assert.Equal([JobRunOutcomes.Success, JobRunOutcomes.Failed, JobRunOutcomes.Success], item.RecentOutcomes);
    }

    [Fact]
    public void Item_running_claim_adds_open_segment()
    {
        var (from, to) = JobsMonitorBuilder.Window(Now);
        var claim = new JobClaimInfo(Now.AddMinutes(-3), "Production@a#1", true, JobOrigins.Prod);
        var card = Card(JobNames.StartList, JobKinds.Schedule, "info", "идёт сейчас",
            extras: new JobMonitorExtras { TimelineMode = JobTimelineModes.Runs, Interval = TimeSpan.FromHours(3), Claim = claim, NextApprox = true });

        var item = JobsMonitorBuilder.Item(card, [Run(1, JobNames.StartList, Now.AddHours(-3))], Now, from, to);

        Assert.Equal(JobStatuses.Running, item.Status);
        var open = item.WindowRuns.Last();
        Assert.Equal(JobRunOutcomes.Running, open.Outcome);
        Assert.Null(open.FinishedAt);
        Assert.Equal(claim.Since, item.StateSince);
        Assert.Equal(JobRunOutcomes.Running, item.RecentOutcomes[0]);
        Assert.True(item.NextApprox);
    }

    [Fact]
    public void Item_overdue_has_since_and_seconds()
    {
        var (from, to) = JobsMonitorBuilder.Window(Now);
        var card = Card(JobNames.SwimmerClubCheck, JobKinds.Schedule, "warning", "просрочена", extras: new JobMonitorExtras
        {
            TimelineMode = JobTimelineModes.Runs, Interval = Day, NextAt = Now.AddMinutes(-78),
        });

        var item = JobsMonitorBuilder.Item(card, [], Now, from, to);

        Assert.Equal(JobStatuses.Overdue, item.Status);
        Assert.Equal(Now.AddMinutes(-78), item.OverdueSince);
        Assert.Equal(78 * 60, item.OverdueSeconds);
        Assert.True(item.DueNow);
    }

    [Fact]
    public void Item_off_has_no_plan_and_ticker_has_no_plan()
    {
        var (from, to) = JobsMonitorBuilder.Window(Now);
        var off = JobsMonitorBuilder.Item(Card("loglig-batch", JobKinds.Schedule, "neutral", "выключена", active: false,
            extras: new JobMonitorExtras { TimelineMode = JobTimelineModes.Runs, Interval = Day }), [], Now, from, to);
        Assert.Equal(JobStatuses.Off, off.Status);
        Assert.Empty(off.Planned);
        Assert.False(off.DueNow);

        // Напоминания: пустые тики не хранятся — ленту рисует клиент, плана меток нет, последний тик — LastRunAt.
        var ticker = JobsMonitorBuilder.Item(Card(JobNames.TrainingReminders, JobKinds.Schedule, "success", "работает",
            extras: new JobMonitorExtras { TimelineMode = JobTimelineModes.Ticker, Interval = TimeSpan.FromMinutes(5),
                LastRunAt = Now.AddMinutes(-4), NextAt = Now.AddMinutes(1) }),
            [Run(9, JobNames.TrainingReminders, Now.AddHours(-2))], Now, from, to);
        Assert.Empty(ticker.Planned);
        Assert.Single(ticker.WindowRuns);
        Assert.Equal(Now.AddMinutes(-4), ticker.LastRunAt);
    }

    [Fact]
    public void Item_queue_uses_runs_from_memory()
    {
        var (from, to) = JobsMonitorBuilder.Window(Now);
        var card = Card("record-country-run", JobKinds.Queue, "info", "идёт прогон", extras: new JobMonitorExtras
        {
            TimelineMode = JobTimelineModes.Runs,
            Progress = new JobProgress(120, 470, "FRA", "стран", "строк 3400"),
            QueueRuns =
            [
                new(null, Now.AddMinutes(-32), null, JobRunOutcomes.Running, JobTriggers.Manual, null, null),
                new(null, Now.AddHours(-5), Now.AddHours(-4), JobRunOutcomes.Success, JobTriggers.Manual, null, null),
                new(null, Now.AddDays(-3), Now.AddDays(-3), JobRunOutcomes.Failed, JobTriggers.Manual, null, null),
            ],
        });

        var item = JobsMonitorBuilder.Item(card, [], Now, from, to);

        Assert.Equal(2, item.WindowRuns.Count);
        Assert.Equal([JobRunOutcomes.Running, JobRunOutcomes.Success, JobRunOutcomes.Failed], item.RecentOutcomes);
        Assert.Empty(item.Planned);
        Assert.Equal(120, item.Progress!.Done);
    }

    // ── Сводка и панели ────────────────────────────────────────────────────────

    [Fact]
    public void Counters_in_design_order_idle_counts_as_ok()
    {
        var c = JobsMonitorBuilder.Counters(
            [JobStatuses.Failed, JobStatuses.Overdue, JobStatuses.Running, JobStatuses.Running,
             JobStatuses.Ok, JobStatuses.Idle, JobStatuses.Idle, JobStatuses.Off], waiting: 1);
        Assert.Equal(new JobsCounters(1, 1, 2, 1, 3, 1), c);
    }

    /// <summary>Сводка страницы и дашборда одна: счётчики считает JobsOverviewBuilder.Build по бейджам карточек.</summary>
    [Fact]
    public void Overview_build_carries_counters()
    {
        var bridge = new CacheBridgeState(true, "swimm_cache", true, Now.AddHours(-1), 0, 3, 0, 0, 5, null, null, null, null);
        var infra = new JobsInfraState(true, false, 0, 0, true, Now, null, bridge);
        var summary = JobsOverviewBuilder.Build(
            [Card("a", JobKinds.Schedule, "danger"), Card("b", JobKinds.Queue, "success"), Card("c", JobKinds.Service, "neutral", active: false)],
            new JobRunStats(0, 0, 0, 0), infra, Now, waiting: 2);
        Assert.Equal(new JobsCounters(1, 0, 0, 2, 1, 1), summary.Counters);
    }

    [Fact]
    public void Attention_lists_failed_abandoned_overdue_and_waiting()
    {
        var (from, to) = JobsMonitorBuilder.Window(Now);
        JobMonitorItem Item(JobCard c) => JobsMonitorBuilder.Item(c, [], Now, from, to);
        var items = new[]
        {
            Item(Card(JobNames.RecordsCheck, JobKinds.Schedule, "danger", extras: new JobMonitorExtras
                { LastError = "worldrecords ответил 503", LastRunAt = Now.AddMinutes(-49) })),
            Item(Card(JobNames.SwimmerClubCheck, JobKinds.Schedule, "warning", extras: new JobMonitorExtras
                { NextAt = Now.AddMinutes(-78), LastSuccessAt = Now.AddHours(-25) })),
            // Брошенная заявка у работающей задачи: статус «в норме», но во «Требует внимания» — да.
            Item(Card(JobNames.Discovery, JobKinds.Schedule, "success", extras: new JobMonitorExtras
                { Claim = new JobClaimInfo(Now.AddHours(-3), "Production@a#1", false, JobOrigins.Prod), Zone = JobZones.Prod })),
            Item(Card(JobNames.ResultsAuto, JobKinds.Schedule, "success", extras: new JobMonitorExtras { Zone = JobZones.Prod })),
            Item(Card("import", JobKinds.Queue, "success")),
        };
        var waiting = new[] { new ResultsAutoWaitingRow(7, 16925, "ליגה", Now.AddDays(-1), Now.AddDays(-1), "бассейн не задан", Now.AddHours(-2)) };

        var list = JobsMonitorBuilder.Attention(items, waiting, Now);

        Assert.Equal([JobAttentionKinds.Failed, JobAttentionKinds.AbandonedClaim, JobAttentionKinds.Overdue, JobAttentionKinds.Waiting],
            list.Select(a => a.Kind));
        Assert.Contains("worldrecords ответил 503", list[0].Text);
        Assert.Equal(Now.AddMinutes(-49), list[0].Since);
        Assert.Equal(JobNames.Discovery, list[1].Key);
        Assert.Equal(JobZones.Prod, list[1].Zone);
        Assert.Contains("Production@a#1", list[1].Text);
        Assert.Contains("1 ч 18 мин", list[2].Text);
        Assert.Equal(Now.AddMinutes(-78), list[2].Since);
        Assert.Equal(JobNames.ResultsAuto, list[3].Key);
        Assert.Equal(JobZones.Prod, list[3].Zone);
        Assert.Contains("#16925", list[3].Text);
        Assert.Contains("бассейн не задан", list[3].Text);
        Assert.StartsWith("/Admin/Competitions?filter=autopull-blocked&q=", list[3].Href);
    }

    [Fact]
    public void Running_and_upcoming_panels()
    {
        var (from, to) = JobsMonitorBuilder.Window(Now);
        JobMonitorItem Item(JobCard c) => JobsMonitorBuilder.Item(c, [], Now, from, to);
        var items = new[]
        {
            Item(Card("start-list", JobKinds.Schedule, "info", extras: new JobMonitorExtras
                { Claim = new JobClaimInfo(Now.AddMinutes(-3), "Production@a#1", true, JobOrigins.Prod), NextAt = Now.AddHours(3), NextApprox = true })),
            Item(Card("record-country-run", JobKinds.Queue, "info", extras: new JobMonitorExtras
                { StateSince = Now.AddMinutes(-32), Progress = new JobProgress(120, 470, "FRA", "стран", null) })),
            Item(Card("records-check", JobKinds.Schedule, "danger", extras: new JobMonitorExtras { NextAt = Now.AddMinutes(11), NextIsRetry = true })),
            Item(Card("discovery", JobKinds.Schedule, "success", extras: new JobMonitorExtras { NextAt = Now.AddHours(17) })),
            Item(Card("loglig-batch", JobKinds.Schedule, "neutral", active: false, extras: new JobMonitorExtras { NextAt = Now.AddHours(1) })),
            Item(Card("overdue", JobKinds.Schedule, "warning", extras: new JobMonitorExtras { NextAt = Now.AddHours(-1) })),
        };

        var running = JobsMonitorBuilder.Running(items);
        Assert.Equal(["record-country-run", "start-list"], running.Select(r => r.Key)); // кто раньше начал
        Assert.Equal("Production@a#1", running[1].By);
        Assert.Null(running[1].Progress); // без оценки
        Assert.Equal(470, running[0].Progress!.Total);

        var upcoming = JobsMonitorBuilder.Upcoming(items, Now);
        Assert.Equal(["records-check", "start-list", "discovery"], upcoming.Select(u => u.Key));
        Assert.True(upcoming[0].IsRetry);
        Assert.True(upcoming[1].Approx);
    }

    [Fact]
    public void Zones_with_pulses_prod_first_and_absent_when_empty()
    {
        var (from, to) = JobsMonitorBuilder.Window(Now);
        var items = new[]
        {
            JobsMonitorBuilder.Item(Card("loglig-verify", JobKinds.Schedule, "success"), [], Now, from, to),
            JobsMonitorBuilder.Item(Card("training-reminders", JobKinds.Schedule, "success",
                extras: new JobMonitorExtras { Zone = JobZones.Prod }), [], Now, from, to),
        };
        var log = new[]
        {
            Run(5, "discovery", Now.AddHours(-1), runBy: "ProdAdmin@VLADDELL#6392"),
            Run(4, "results-auto", Now.AddHours(-2), runBy: "Production@new#1901"),
            Run(3, "results-auto", Now.AddHours(-5), runBy: "Production@old#17"),
        };

        var zones = JobsMonitorBuilder.Zones(items, log, "ProdAdmin@VLADDELL#6392", Now.AddHours(-5), Now.AddMinutes(-4));

        Assert.Equal([JobZones.Prod, JobZones.Local], zones.Select(z => z.Key));
        Assert.Equal("Production@new#1901", zones[0].Process);
        Assert.Equal(Now.AddMinutes(-4), zones[0].PulseAt);
        Assert.Equal("tick", zones[0].PulseKind);
        Assert.Equal(["training-reminders"], zones[0].JobKeys);
        Assert.Equal("ProdAdmin@VLADDELL#6392", zones[1].Process);
        Assert.Equal("started", zones[1].PulseKind);

        // Development: всё local — рамки прод нет.
        var dev = JobsMonitorBuilder.Zones([items[0]], log, "Development@x#1", Now, null);
        Assert.Equal([JobZones.Local], dev.Select(z => z.Key));
    }

    [Fact]
    public void Build_assembles_everything_with_summary_counters()
    {
        var bridge = new CacheBridgeState(true, "swimm_cache", true, Now.AddHours(-1), 0, 3, 0, 0, 5, null, null, null, null);
        var infra = new JobsInfraState(true, true, 0, 0, true, Now, null, bridge);
        JobCard[] cards = [Card("discovery", JobKinds.Schedule, "success"), Card("import", JobKinds.Queue, "success")];
        var summary = JobsOverviewBuilder.Build(cards, new JobRunStats(0, 0, 0, 0), infra, Now, waiting: 0);

        var monitor = JobsMonitorBuilder.Build(new JobsMonitorInput(Now, "Development", "local", cards,
            [Run(1, "discovery", Now.AddHours(-1))], summary, [], "Development@x#1", Now.AddHours(-2), null, null, []));

        Assert.Equal(JobsMonitorBuilder.WindowHours, monitor.WindowHours);
        Assert.Same(summary.Counters, monitor.Counters);
        Assert.Equal(2, monitor.Jobs.Count);
        Assert.Single(monitor.Jobs[0].History);
        Assert.Empty(monitor.Jobs[1].History);
        Assert.Equal([JobZones.Local], monitor.Zones.Select(z => z.Key));
    }

    // ── «Ждёт тебя» и статистика ───────────────────────────────────────────────

    [Theory]
    [InlineData(ResultsAutoVerdicts.Blocked, -2, DiscoveredCompetitionStatus.New, false, true)]
    [InlineData(ResultsAutoVerdicts.Blocked, -25, DiscoveredCompetitionStatus.New, false, false)] // старше суток
    [InlineData(ResultsAutoVerdicts.Blocked, -2, DiscoveredCompetitionStatus.New, true, false)]   // уже затянули руками
    [InlineData(ResultsAutoVerdicts.Blocked, -2, DiscoveredCompetitionStatus.Ignored, false, false)]
    [InlineData(ResultsAutoVerdicts.WouldTake, -2, DiscoveredCompetitionStatus.New, false, false)]
    public void Waiting_rule(string verdict, int checkedHoursAgo, string status, bool inDb, bool expected) =>
        Assert.Equal(expected, ResultsAutoWaiting.IsWaiting(verdict, Now.AddHours(checkedHoursAgo), status, inDb, Now));

    // Застряло: «ещё идёт» / «протокола нет», а старт кончился больше полутора суток назад (16903, 21.11.2026).
    [Theory]
    [InlineData(ResultsAutoVerdicts.NotFinished, -37, DiscoveredCompetitionStatus.New, false, true)]
    [InlineData(ResultsAutoVerdicts.Empty, -37, DiscoveredCompetitionStatus.New, false, true)]
    [InlineData(ResultsAutoVerdicts.NotFinished, -35, DiscoveredCompetitionStatus.New, false, false)]   // ещё честно идёт
    [InlineData(ResultsAutoVerdicts.NotFinished, -24 * 9, DiscoveredCompetitionStatus.New, false, false)] // окно задачи закрылось
    [InlineData(ResultsAutoVerdicts.NotFinished, -37, DiscoveredCompetitionStatus.New, true, false)]    // уже затянули руками
    [InlineData(ResultsAutoVerdicts.Empty, -37, DiscoveredCompetitionStatus.Ignored, false, false)]
    [InlineData(ResultsAutoVerdicts.Blocked, -37, DiscoveredCompetitionStatus.New, false, false)]       // это IsWaiting
    [InlineData(ResultsAutoVerdicts.WouldTake, -37, DiscoveredCompetitionStatus.New, false, false)]
    public void Stuck_rule(string verdict, int endedHoursAgo, string status, bool inDb, bool expected) =>
        Assert.Equal(expected, ResultsAutoWaiting.IsStuck(verdict, Now.AddHours(endedHoursAgo), status, inDb, Now));

    [Fact]
    public void Attention_stuck_rows_say_what_is_stuck_and_link_without_blocked_filter()
    {
        var ended = Now.AddDays(-2);
        var waiting = new[]
        {
            new ResultsAutoWaitingRow(1, 16903, "ליגה", ended, ended, "ждут 2 из 16 событий", Now.AddHours(-1), ResultsAutoVerdicts.NotFinished),
            new ResultsAutoWaitingRow(2, 16897, "הרצליה", ended, ended, "No competitions found in PDF", Now.AddHours(-3), ResultsAutoVerdicts.Empty),
        };

        var list = JobsMonitorBuilder.Attention([], waiting, Now);

        Assert.All(list, a => Assert.Equal(JobAttentionKinds.Waiting, a.Kind));
        Assert.Contains("результаты до сих пор неполные", list[0].Text);
        Assert.Contains("ждут 2 из 16 событий", list[0].Text);
        Assert.Contains($"{ended:dd.MM}", list[0].Text);
        Assert.Contains("протокола результатов до сих пор нет", list[1].Text);
        Assert.All(list, a => Assert.StartsWith("/Admin/Competitions?q=", a.Href));
    }

    [Fact]
    public void Waiting_rule_unchecked_row_never_waits() =>
        Assert.False(ResultsAutoWaiting.IsWaiting(ResultsAutoVerdicts.Blocked, null, DiscoveredCompetitionStatus.New, false, Now));

    [Fact]
    public void Run_stats_from_rows()
    {
        var stats = JobRunStats.From(
        [
            (Now.AddHours(-1), true), (Now.AddHours(-2), false), (Now.AddDays(-3), false), (Now.AddDays(-8), false),
        ], Now);
        Assert.Equal(new JobRunStats(2, 1, 3, 2), stats);
    }
}
