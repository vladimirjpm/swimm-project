using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task Store_records_success_and_failure()
    {
        await using var db = CreateDb(nameof(Store_records_success_and_failure));
        var store = new JobRunStore(db);

        var empty = await store.GetAsync(JobNames.Discovery);
        Assert.Null(empty.LastAttemptAt);
        Assert.Null(empty.LastSuccessAt);

        var t1 = Now.AddHours(-25);
        await store.RecordAsync(JobNames.Discovery, t1, success: true, error: null);
        var t2 = Now.AddMinutes(-5);
        await store.RecordAsync(JobNames.Discovery, t2, success: false, error: new string('x', 1500));

        var state = await store.GetAsync(JobNames.Discovery);
        Assert.Equal(t2, state.LastAttemptAt);
        Assert.Equal(t1, state.LastSuccessAt);
        var row = await db.JobRuns.AsNoTracking().SingleAsync();
        Assert.Equal(1000, row.LastError!.Length);

        // Только что упала — ждём; успех снова стирает ошибку.
        Assert.False(JobSchedule.IsDue(Now, state.LastSuccessAt, state.LastAttemptAt, TimeSpan.FromHours(24), JobSchedule.RetryAfterFailure));
        await store.RecordAsync(JobNames.Discovery, Now, success: true, error: null);
        row = await db.JobRuns.AsNoTracking().SingleAsync();
        Assert.Null(row.LastError);
        Assert.Equal(Now, row.LastSuccessAt);
    }
}
