using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// «Пора ли запускать» фоновое расписание (В12, docs/plans/competitions-home-plan.md, часть В):
/// последний запуск — из журнала Sys_JobRuns, поэтому рестарт API задачу заново не гонит.
/// </summary>
public class JobScheduleTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);
    private static readonly TimeSpan Retry = JobSchedule.RetryAfterFailure;

    [Fact]
    public void Never_ran_is_due() =>
        Assert.True(JobSchedule.IsDue(Now, null, null, Day, Retry));

    [Fact]
    public void Success_less_than_interval_ago_is_not_due()
    {
        var ok = Now.AddHours(-23);
        Assert.False(JobSchedule.IsDue(Now, ok, ok, Day, Retry));
    }

    [Fact]
    public void Success_interval_ago_is_due()
    {
        var ok = Now.AddHours(-24);
        Assert.True(JobSchedule.IsDue(Now, ok, ok, Day, Retry));
    }

    [Fact]
    public void Failure_waits_retry_not_interval()
    {
        // Последний успех давно, последняя попытка упала 30 минут назад — ждём час, не сутки и не 5 минут.
        var ok = Now.AddDays(-3);
        Assert.False(JobSchedule.IsDue(Now, ok, Now.AddMinutes(-30), Day, Retry));
        Assert.True(JobSchedule.IsDue(Now, ok, Now.AddMinutes(-60), Day, Retry));
    }

    [Fact]
    public void Failure_without_any_success_waits_retry()
    {
        Assert.False(JobSchedule.IsDue(Now, null, Now.AddMinutes(-10), Day, Retry));
        Assert.True(JobSchedule.IsDue(Now, null, Now.AddHours(-2), Day, Retry));
    }

    [Fact]
    public void Retry_after_failure_is_one_hour() =>
        Assert.Equal(TimeSpan.FromHours(1), JobSchedule.RetryAfterFailure);

    [Fact]
    public void Job_names_are_stable_keys()
    {
        // Ключи строк журнала в базе: переименование = задача «никогда не запускалась» и сразу пойдёт наружу.
        Assert.Equal("discovery", JobNames.Discovery);
        Assert.Equal("start-list", JobNames.StartList);
        Assert.Equal("loglig-verify", JobNames.LogligVerify);
        Assert.Equal("loglig-batch", JobNames.LogligBatch);
    }

    // ── Журнал (JobRunStore) ────────────────────────────────────────────────────

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static JobRunRecord Run(string name, DateTime at, bool success = true, string? summary = "итог",
        string? error = null, string trigger = JobTriggers.Schedule) =>
        new(name, trigger, at, at.AddMinutes(1), success, summary, error);

    [Fact]
    public async Task Store_records_success_and_failure()
    {
        await using var db = CreateDb(nameof(Store_records_success_and_failure));
        var store = new JobRunStore(db);

        var empty = await store.GetAsync(JobNames.Discovery);
        Assert.Null(empty.LastAttemptAt);
        Assert.Null(empty.LastSuccessAt);

        var t1 = Now.AddHours(-25);
        await store.RecordAsync(Run(JobNames.Discovery, t1));
        var t2 = Now.AddMinutes(-5);
        await store.RecordAsync(Run(JobNames.Discovery, t2, success: false, summary: null, error: new string('x', 1500)));

        var state = await store.GetAsync(JobNames.Discovery);
        Assert.Equal(t2, state.LastAttemptAt);
        Assert.Equal(t1, state.LastSuccessAt);
        var row = await db.JobRuns.AsNoTracking().SingleAsync();
        Assert.Equal(1000, row.LastError!.Length);

        // Только что упала — ждём; успех снова стирает ошибку.
        Assert.False(JobSchedule.IsDue(Now, state.LastSuccessAt, state.LastAttemptAt, TimeSpan.FromHours(24), JobSchedule.RetryAfterFailure));
        await store.RecordAsync(Run(JobNames.Discovery, Now));
        row = await db.JobRuns.AsNoTracking().SingleAsync();
        Assert.Null(row.LastError);
        Assert.Equal(Now, row.LastSuccessAt);
    }

    // ── История прогонов (Sys_JobRunLog, /Admin/Jobs) ──────────────────────────

    [Fact]
    public async Task Record_appends_history_with_summary_and_error()
    {
        await using var db = CreateDb(nameof(Record_appends_history_with_summary_and_error));
        var store = new JobRunStore(db);

        await store.RecordAsync(Run(JobNames.StartList, Now.AddHours(-2), summary: "протоколов забрано 3 из 4"));
        await store.RecordAsync(Run(JobNames.StartList, Now, success: false, summary: null, error: "502 Bad Gateway"));

        var log = await store.ListLogAsync(perJob: 10);
        Assert.Equal(2, log.Count);
        // Свежие первыми.
        Assert.False(log[0].Success);
        Assert.Equal("502 Bad Gateway", log[0].Error);
        Assert.Null(log[0].Summary);
        Assert.True(log[1].Success);
        Assert.Equal("протоколов забрано 3 из 4", log[1].Summary);
        Assert.Null(log[1].Error);
        Assert.Equal(Now.AddHours(-2).AddMinutes(1), log[1].FinishedAt);
    }

    [Fact]
    public async Task Failure_without_text_gets_placeholder_and_success_drops_error()
    {
        await using var db = CreateDb(nameof(Failure_without_text_gets_placeholder_and_success_drops_error));
        var store = new JobRunStore(db);

        await store.AppendLogAsync(Run(JobNames.Discovery, Now.AddHours(-1), success: false, error: "  "));
        // Ошибку у успешного прогона не храним — даже если её передали.
        await store.AppendLogAsync(Run(JobNames.Discovery, Now, success: true, error: "лишнее"));

        var log = await store.ListLogAsync(10);
        Assert.Null(log[0].Error);
        Assert.Equal("ошибка без текста", log[1].Error);
    }

    [Fact]
    public async Task AppendLog_does_not_move_schedule()
    {
        // Ручной прогон прошлого сезона или упавшая кнопка: строка истории есть, расписание то же.
        await using var db = CreateDb(nameof(AppendLog_does_not_move_schedule));
        var store = new JobRunStore(db);

        await store.RecordAsync(Run(JobNames.Discovery, Now.AddHours(-30)));
        await store.AppendLogAsync(Run(JobNames.Discovery, Now, success: false, error: "сайт лёг", trigger: JobTriggers.Manual));

        var state = await store.GetAsync(JobNames.Discovery);
        Assert.Equal(Now.AddHours(-30), state.LastAttemptAt);
        Assert.Equal(Now.AddHours(-30), state.LastSuccessAt);
        Assert.Null((await db.JobRuns.AsNoTracking().SingleAsync()).LastError);

        var log = await store.ListLogAsync(10);
        Assert.Equal(2, log.Count);
        Assert.Equal(JobTriggers.Manual, log[0].Trigger);
    }

    [Fact]
    public async Task History_is_trimmed_per_job()
    {
        await using var db = CreateDb(nameof(History_is_trimmed_per_job));
        var store = new JobRunStore(db);

        const int extra = 3;
        for (var i = 0; i < JobRunLog.KeepPerJob + extra; i++)
            await store.AppendLogAsync(Run(JobNames.LogligVerify, Now.AddHours(-100 + i)));
        // Другая задача обрезкой не задета.
        await store.AppendLogAsync(Run(JobNames.LogligBatch, Now.AddDays(-365)));

        Assert.Equal(JobRunLog.KeepPerJob, await db.JobRunLog.CountAsync(e => e.Name == JobNames.LogligVerify));
        Assert.Equal(1, await db.JobRunLog.CountAsync(e => e.Name == JobNames.LogligBatch));

        // Ушли самые старые.
        var oldestKept = await db.JobRunLog.Where(e => e.Name == JobNames.LogligVerify).MinAsync(e => e.StartedAt);
        Assert.Equal(Now.AddHours(-100 + extra), oldestKept);
    }

    [Fact]
    public async Task ListLog_takes_last_n_of_each_job()
    {
        await using var db = CreateDb(nameof(ListLog_takes_last_n_of_each_job));
        var store = new JobRunStore(db);

        for (var i = 0; i < 5; i++)
        {
            await store.AppendLogAsync(Run(JobNames.Discovery, Now.AddHours(-i)));
            await store.AppendLogAsync(Run(JobNames.StartList, Now.AddHours(-i)));
        }

        var log = await store.ListLogAsync(perJob: 2);
        Assert.Equal(2, log.Count(r => r.Name == JobNames.Discovery));
        Assert.Equal(2, log.Count(r => r.Name == JobNames.StartList));
        Assert.All(log, r => Assert.True(r.StartedAt >= Now.AddHours(-1)));
    }

    [Fact]
    public async Task Stats_count_runs_for_day_and_week()
    {
        await using var db = CreateDb(nameof(Stats_count_runs_for_day_and_week));
        var store = new JobRunStore(db);

        await store.AppendLogAsync(Run(JobNames.Discovery, Now.AddHours(-1)));
        await store.AppendLogAsync(Run(JobNames.StartList, Now.AddHours(-23), success: false, error: "502"));
        await store.AppendLogAsync(Run(JobNames.LogligVerify, Now.AddDays(-3), success: false, error: "таймаут"));
        await store.AppendLogAsync(Run(JobNames.LogligVerify, Now.AddDays(-6)));
        await store.AppendLogAsync(Run(JobNames.Discovery, Now.AddDays(-8))); // старше недели — не в счёт

        Assert.Equal(new JobRunStats(Runs24h: 2, Failed24h: 1, Runs7d: 4, Failed7d: 2), await store.GetStatsAsync(Now));
    }

    [Fact]
    public async Task ListStates_returns_every_job_row()
    {
        await using var db = CreateDb(nameof(ListStates_returns_every_job_row));
        var store = new JobRunStore(db);

        await store.RecordAsync(Run(JobNames.LogligVerify, Now));
        await store.RecordAsync(Run(JobNames.Discovery, Now, success: false, error: "нет сети"));

        var states = await store.ListStatesAsync();
        Assert.Equal([JobNames.Discovery, JobNames.LogligVerify], states.Select(s => s.Name));
        Assert.Equal("нет сети", states[0].LastError);
        Assert.Null(states[0].LastSuccessAt);
    }

    // ── Когда следующий (NextDueAt) ────────────────────────────────────────────

    [Fact]
    public void NextDue_never_ran_is_now() =>
        Assert.Null(JobSchedule.NextDueAt(null, null, Day, Retry));

    [Fact]
    public void NextDue_after_success_is_interval_later()
    {
        var ok = Now.AddHours(-5);
        Assert.Equal(ok + Day, JobSchedule.NextDueAt(ok, ok, Day, Retry));
    }

    [Fact]
    public void NextDue_after_failure_is_retry_later()
    {
        var failedAt = Now.AddMinutes(-10);
        Assert.Equal(failedAt + Retry, JobSchedule.NextDueAt(Now.AddDays(-2), failedAt, Day, Retry));
        Assert.Equal(failedAt + Retry, JobSchedule.NextDueAt(null, failedAt, Day, Retry));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(59)]
    [InlineData(60)]
    [InlineData(61)]
    [InlineData(60 * 24)]
    [InlineData(60 * 30)]
    public void NextDue_agrees_with_IsDue(int minutesAgo)
    {
        // Одно правило в двух видах: «пора» ровно тогда, когда момент NextDueAt наступил.
        var at = Now.AddMinutes(-minutesAgo);
        foreach (var (ok, attempt) in new (DateTime?, DateTime?)[] { (at, at), (Now.AddDays(-5), at), (null, at) })
        {
            var next = JobSchedule.NextDueAt(ok, attempt, Day, Retry);
            Assert.Equal(next is null || next <= Now, JobSchedule.IsDue(Now, ok, attempt, Day, Retry));
        }
    }

    // ── Определения задач и итоги ──────────────────────────────────────────────

    [Fact]
    public void Every_job_has_definition_with_stable_defaults()
    {
        Assert.Equal(
            [JobNames.Discovery, JobNames.StartList, JobNames.LogligVerify, JobNames.LogligBatch],
            JobNames.All.Select(j => j.Name));
        // По умолчанию включена только сверка loglig-id: без неё предложения висели бы вечно.
        Assert.Equal([JobNames.LogligVerify], JobNames.All.Where(j => j.EnabledDefault).Select(j => j.Name));
        // Стартовый протокол живёт на интервале discovery (С10, решение 5).
        Assert.Equal(JobNames.Get(JobNames.Discovery).IntervalKey, JobNames.Get(JobNames.StartList).IntervalKey);
    }

    [Fact]
    public void Interval_reads_setting_and_is_at_least_an_hour()
    {
        var settings = new AdminSettingsService(new MemoryCache(new MemoryCacheOptions()));
        var job = JobNames.Get(JobNames.LogligVerify);
        Assert.True(job.IsEnabled(settings));
        Assert.Equal(TimeSpan.FromHours(24), job.Interval(settings));

        Assert.True(settings.Update(job.IntervalKey, "6"));
        Assert.Equal(TimeSpan.FromHours(6), job.Interval(settings));
        Assert.True(settings.Update(job.IntervalKey, "0"));
        Assert.Equal(TimeSpan.FromHours(1), job.Interval(settings));
    }

    [Fact]
    public void Summaries_say_what_the_run_did()
    {
        var discovery = JobRunSummaries.Discovery(new DiscoverySyncResult
            { TotalOnSite = 41, Added = 2, Updated = 5, Postponed = 1, Missing = 0 });
        Assert.Equal("на сайте 41, новых 2, обновлено 5, перенесено 1, пропало 0", discovery);

        Assert.Equal("окно 14 дн.: loglig-id добыт у 1 из 3, протоколов забрано 4 из 5",
            JobRunSummaries.StartList(new StartListSweepReport(3, 1, 5, 4), 14));
        Assert.Equal("предложений нет",
            JobRunSummaries.LogligVerify(new LogligSuggestionVerifyReport(0, 0, 0, 0)));
        Assert.Equal("проверено 4, подтверждено 2, отклонено 1, отложено 1",
            JobRunSummaries.LogligVerify(new LogligSuggestionVerifyReport(4, 2, 1, 1)));
        Assert.Equal("лимит 50: обработано 50, привязано 7, с кандидатами 10, впустую 33",
            JobRunSummaries.LogligBatch(new LogligBatchReport(50, 7, 10, 33), 50));
    }
}
