using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// История прогонов фоновых задач (<c>Sys_JobRunLog</c>, /Admin/Jobs) в НАСТОЯЩЕМ Postgres под
/// рантайм-ролью swimm_rw: InMemory не видит CHECK на «кто запустил», длину колонок и права роли.
/// Задачи у теста свои (<c>utest-…</c>), строки удаляются в finally. Пропуск, если PG недоступен
/// или миграция AddJobRunLog ещё не применена.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class JobRunStorePgTests
{
    private const string Runtime =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm_rw;Password=swimm_rw_local_dev";

    private static async Task<SwimmDbContext?> TryCreate()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Runtime).Options);
        try
        {
            if (!await db.Database.CanConnectAsync()) { await db.DisposeAsync(); return null; }
            await db.JobRunLog.AnyAsync(); // таблицы нет — миграция не применена
            return db;
        }
        catch { await db.DisposeAsync(); return null; }
    }

    private static async Task Cleanup(SwimmDbContext db, string name)
    {
        db.ChangeTracker.Clear();
        await db.JobRunLog.Where(e => e.Name == name).ExecuteDeleteAsync();
        await db.JobRuns.Where(e => e.Name == name).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Record_and_trim_by_runtime_role()
    {
        await using var db = await TryCreate();
        if (db is null) return;

        var name = "utest-job-" + Guid.NewGuid().ToString("N")[..8];
        var store = new JobRunStore(db);
        var start = DateTime.UtcNow.AddDays(-10);
        try
        {
            for (var i = 0; i < JobRunLog.KeepPerJob + 2; i++)
                await store.RecordAsync(new JobRunRecord(name, JobTriggers.Schedule, start.AddHours(i),
                    start.AddHours(i).AddMinutes(1), Success: i % 7 != 0,
                    Summary: new string('и', 1500), Error: i % 7 == 0 ? "502" : null));

            db.ChangeTracker.Clear();
            Assert.Equal(JobRunLog.KeepPerJob, await db.JobRunLog.CountAsync(e => e.Name == name));
            // Итог длиннее колонки обрезан, а не уронил запись.
            Assert.All(await db.JobRunLog.Where(e => e.Name == name).Select(e => e.Summary).ToListAsync(),
                s => Assert.Equal(JobRunLog.MaxTextLength, s!.Length));

            var last = (await store.ListLogAsync(1)).Single(r => r.Name == name);
            Assert.Equal(start.AddHours(JobRunLog.KeepPerJob + 1), last.StartedAt, TimeSpan.FromMilliseconds(1));
            Assert.NotNull((await store.ListStatesAsync()).SingleOrDefault(s => s.Name == name));
        }
        finally
        {
            await Cleanup(db, name);
        }
    }

    /// <summary>
    /// Подробности прогона (jsonb <c>Details</c>, миграция AddJobRunLogDetails) пишутся рантайм-
    /// ролью и читаются обратно: попап «что изменилось» на /Admin/Jobs. Иврит и кавычки в имени
    /// старта переживают jsonb (он переформатирует JSON — сравниваем разобранное, не текст).
    /// </summary>
    [Fact]
    public async Task Details_jsonb_round_trip()
    {
        await using var db = await TryCreate();
        if (db is null) return;

        var name = "utest-job-" + Guid.NewGuid().ToString("N")[..8];
        var store = new JobRunStore(db);
        var result = new Swimm.Application.Dtos.DiscoverySyncResult();
        var date = new DateTime(2026, 12, 5, 0, 0, 0, DateTimeKind.Utc);
        result.Changes.Add(new(Swimm.Application.Dtos.DiscoveryChangeKinds.Postponed, 777, "אליפות \"חורף\"",
            date.AddDays(7), date.AddDays(8), OldDateStart: date, OldDateEnd: date.AddDays(1)));
        try
        {
            await store.AppendLogAsync(new JobRunRecord(name, JobTriggers.Manual, DateTime.UtcNow, DateTime.UtcNow,
                Success: true, "перенесено 1", Error: null, Details: JobRunDetails.Discovery(result)));
            await store.AppendLogAsync(new JobRunRecord(name, JobTriggers.Manual, DateTime.UtcNow.AddMinutes(-1),
                DateTime.UtcNow, Success: true, "без изменений", Error: null));

            db.ChangeTracker.Clear();
            var rows = (await store.ListLogAsync(5)).Where(r => r.Name == name).ToList();
            var parsed = JobRunDetails.ParseDiscovery(rows.Single(r => r.Summary == "перенесено 1").Details);
            Assert.NotNull(parsed);
            Assert.Equal(result.Changes[0], Assert.Single(parsed.Changes));
            Assert.Null(rows.Single(r => r.Summary == "без изменений").Details);
        }
        finally
        {
            await Cleanup(db, name);
        }
    }

    [Fact]
    public async Task Unknown_trigger_is_rejected_by_check()
    {
        await using var db = await TryCreate();
        if (db is null) return;

        var name = "utest-job-" + Guid.NewGuid().ToString("N")[..8];
        var store = new JobRunStore(db);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => store.AppendLogAsync(new JobRunRecord(
                name, "cron", DateTime.UtcNow, DateTime.UtcNow, Success: true, Summary: null, Error: null)));
        }
        finally
        {
            await Cleanup(db, name);
        }
    }
}
