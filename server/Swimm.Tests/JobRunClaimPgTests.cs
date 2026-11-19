using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.API.BackgroundServices;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Замок прогона фоновой задачи (П1, docs/plans/prod-auto-pull-plan.md §4.1) в НАСТОЯЩЕМ Postgres
/// под рантайм-ролью swimm_rw: «заявку получает ровно один» держится на блокировке строки и
/// <c>ON CONFLICT … WHERE</c> — InMemory этого не проверяет вообще. Каждый «процесс» в тесте —
/// свой DbContext (своё соединение). Задачи у теста свои (<c>utest-…</c>), строки удаляются в
/// finally. Пропуск, если PG недоступен или миграция AddJobRunClaim ещё не применена.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class JobRunClaimPgTests
{
    private const string Runtime =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm_rw;Password=swimm_rw_local_dev";

    private static readonly TimeSpan Stale = JobSchedule.ClaimStaleAfter;

    private static SwimmDbContext NewDb() =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Runtime).Options);

    private static async Task<bool> Available()
    {
        await using var db = NewDb();
        try
        {
            if (!await db.Database.CanConnectAsync()) return false;
            await db.JobRuns.Select(j => j.ClaimedAt).AnyAsync(); // колонки нет — миграция не применена
            return true;
        }
        catch { return false; }
    }

    private static string NewName() => "utest-claim-" + Guid.NewGuid().ToString("N")[..8];

    private static async Task Cleanup(string name)
    {
        await using var db = NewDb();
        await db.JobRunLog.Where(e => e.Name == name).ExecuteDeleteAsync();
        await db.JobRuns.Where(e => e.Name == name).ExecuteDeleteAsync();
    }

    private static async Task<JobClaimResult> Claim(string name, string owner)
    {
        await using var db = NewDb();
        return await new JobRunStore(db).TryClaimAsync(name, owner, Stale);
    }

    private static async Task Release(string name, string owner)
    {
        await using var db = NewDb();
        await new JobRunStore(db).ReleaseClaimAsync(name, owner);
    }

    /// <summary>Двенадцать «процессов» разом, строки задачи ещё нет: заявка достаётся ровно одному.</summary>
    [Fact]
    public async Task Concurrent_claims_give_exactly_one_winner()
    {
        if (!await Available()) return;
        var name = NewName();
        try
        {
            var start = new TaskCompletionSource();
            var attempts = Enumerable.Range(0, 12).Select(async i =>
            {
                await start.Task;
                return (Owner: $"p{i}", Result: await Claim(name, $"p{i}"));
            }).ToList();
            start.SetResult();
            var results = await Task.WhenAll(attempts);

            var winner = Assert.Single(results, r => r.Result.Taken);
            await using var db = NewDb();
            var row = await db.JobRuns.AsNoTracking().SingleAsync(j => j.Name == name);
            Assert.Equal(winner.Owner, row.ClaimedBy);
            Assert.NotNull(row.ClaimedAt);

            // Повтор, когда строка уже есть (ветка ON CONFLICT): снова никому, и видно, кто держит.
            var second = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Claim(name, $"q{i}")));
            Assert.All(second, r =>
            {
                Assert.False(r.Taken);
                Assert.Equal(winner.Owner, r.HeldBy);
                Assert.NotNull(r.HeldSince);
            });
        }
        finally { await Cleanup(name); }
    }

    [Fact]
    public async Task Release_frees_the_claim_only_for_its_owner()
    {
        if (!await Available()) return;
        var name = NewName();
        try
        {
            Assert.True((await Claim(name, "prod")).Taken);

            await Release(name, "prodadmin"); // чужая — не снимается
            Assert.False((await Claim(name, "prodadmin")).Taken);

            await Release(name, "prod");
            Assert.True((await Claim(name, "prodadmin")).Taken);
        }
        finally { await Cleanup(name); }
    }

    /// <summary>Процесс убили посреди прогона: заявка старше <see cref="JobSchedule.ClaimStaleAfter"/> перехватывается.</summary>
    [Fact]
    public async Task Stale_claim_is_taken_over_and_old_owner_cannot_release_it()
    {
        if (!await Available()) return;
        var name = NewName();
        try
        {
            Assert.True((await Claim(name, "killed")).Taken);
            await using (var db = NewDb())
            {
                // Почти протухла — ещё держит.
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE "Sys_JobRuns" SET "ClaimedAt" = now() - {Stale - TimeSpan.FromMinutes(5)} WHERE "Name" = {name}""");
                Assert.False((await Claim(name, "next")).Taken);

                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE "Sys_JobRuns" SET "ClaimedAt" = now() - {Stale + TimeSpan.FromMinutes(5)} WHERE "Name" = {name}""");
            }

            Assert.True((await Claim(name, "next")).Taken);

            // Старый владелец «ожил» и снимает свою заявку — новую это не трогает.
            await Release(name, "killed");
            var held = await Claim(name, "third");
            Assert.False(held.Taken);
            Assert.Equal("next", held.HeldBy);
        }
        finally { await Cleanup(name); }
    }

    /// <summary>Запись итога прогона заявку не стирает и сама ею не затирается.</summary>
    [Fact]
    public async Task Recording_a_run_keeps_the_claim()
    {
        if (!await Available()) return;
        var name = NewName();
        try
        {
            Assert.True((await Claim(name, "prod")).Taken);
            var at = DateTime.UtcNow;
            await using (var db = NewDb())
                await new JobRunStore(db).RecordAsync(new JobRunRecord(
                    name, JobTriggers.Schedule, at, at.AddSeconds(5), Success: true, "итог", Error: null));

            await using var read = NewDb();
            var row = (await new JobRunStore(read).ListStatesAsync()).Single(s => s.Name == name);
            Assert.Equal("prod", row.ClaimedBy);
            Assert.NotNull(row.ClaimedAt);
            Assert.NotNull(row.LastSuccessAt);
        }
        finally { await Cleanup(name); }
    }

    // ── ScheduledJob целиком: два «процесса» на одной базе ─────────────────────

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Swimm.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ServiceProvider Process(string environment) =>
        new ServiceCollection()
            .AddDbContext<SwimmDbContext>(o => o.UseNpgsql(Runtime))
            .AddScoped<IJobRunStore, JobRunStore>()
            .AddSingleton<IHostEnvironment>(new Env(environment))
            .BuildServiceProvider();

    /// <summary>
    /// Критерий готовности П1: двум процессам пора одновременно — работает один, второй видит
    /// «занято» и не запускает работу; после прогона заявка снята, а задаче уже не пора.
    /// </summary>
    [Fact]
    public async Task Two_processes_due_at_once_run_the_work_once()
    {
        if (!await Available()) return;
        var name = NewName();
        await using var prod = Process("Production");
        await using var prodAdmin = Process("ProdAdmin");
        try
        {
            var runs = 0;
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<string?> Work(CancellationToken _)
            {
                Interlocked.Increment(ref runs);
                started.TrySetResult();
                await finish.Task;
                return "сделано";
            }

            Task<bool> Tick(ServiceProvider process) => ScheduledJob.RunIfDueAsync(
                process.GetRequiredService<IServiceScopeFactory>(), name, TimeSpan.FromHours(24),
                Work, NullLogger.Instance, CancellationToken.None);

            var first = Tick(prod);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30));

            // Первый ещё работает (итог не записан — «пора» для второго по-прежнему правда).
            Assert.False(await Tick(prodAdmin));
            Assert.Equal(1, runs);
            await using (var db = NewDb())
            {
                var busy = await db.JobRuns.AsNoTracking().SingleAsync(j => j.Name == name);
                Assert.StartsWith("Production@", busy.ClaimedBy);
                Assert.Null(busy.LastAttemptAt);
            }

            finish.SetResult();
            Assert.True(await first);

            await using (var db = NewDb())
            {
                var done = await db.JobRuns.AsNoTracking().SingleAsync(j => j.Name == name);
                Assert.Null(done.ClaimedAt);
                Assert.Null(done.ClaimedBy);
                Assert.NotNull(done.LastSuccessAt);
                Assert.Equal("сделано", (await db.JobRunLog.AsNoTracking().SingleAsync(e => e.Name == name)).Summary);
            }

            // Замок свободен, но задаче уже не пора — второй процесс её не повторяет.
            Assert.False(await Tick(prodAdmin));
            Assert.Equal(1, runs);
        }
        finally { await Cleanup(name); }
    }

    /// <summary>Работа упала или приложение остановили — заявка всё равно снята.</summary>
    [Fact]
    public async Task Claim_is_released_after_failure_and_after_cancellation()
    {
        if (!await Available()) return;
        var name = NewName();
        await using var prod = Process("Production");
        var scopes = prod.GetRequiredService<IServiceScopeFactory>();
        try
        {
            Assert.True(await ScheduledJob.RunIfDueAsync(scopes, name, TimeSpan.FromHours(24),
                (Func<CancellationToken, Task<string?>>)(_ => throw new InvalidOperationException("502")),
                NullLogger.Instance, CancellationToken.None));
            await using (var db = NewDb())
            {
                var failed = await db.JobRuns.AsNoTracking().SingleAsync(j => j.Name == name);
                Assert.Null(failed.ClaimedAt);
                Assert.Equal("502", failed.LastError);
                // Чтобы задаче снова стало «пора» (после неудачи — через час).
                await db.JobRunLog.Where(e => e.Name == name).ExecuteDeleteAsync();
                await db.JobRuns.Where(e => e.Name == name).ExecuteDeleteAsync();
            }

            using var cts = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ScheduledJob.RunIfDueAsync(
                scopes, name, TimeSpan.FromHours(24),
                async ct =>
                {
                    cts.Cancel();
                    await Task.Delay(Timeout.Infinite, ct);
                    return (string?)null;
                }, NullLogger.Instance, cts.Token));

            await using (var db = NewDb())
            {
                var stopped = await db.JobRuns.AsNoTracking().SingleAsync(j => j.Name == name);
                Assert.Null(stopped.ClaimedAt);
                Assert.Null(stopped.LastAttemptAt); // остановку не записываем — следующий тик повторит
            }
        }
        finally { await Cleanup(name); }
    }
}
