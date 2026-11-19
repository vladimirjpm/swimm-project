using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Журнал запусков фоновых задач в <c>Sys_JobRuns</c> (В12) и история прогонов с итогом в
/// <c>Sys_JobRunLog</c> (/Admin/Jobs). Пишет <see cref="SwimmDbContext"/> (рантайм-роль swimm_rw):
/// таблицы служебные, публичному пути их не видно.
/// </summary>
public class JobRunStore(SwimmDbContext db) : IJobRunStore
{
    public async Task<JobRunState> GetAsync(string name, CancellationToken ct = default)
    {
        var row = await db.JobRuns.AsNoTracking().FirstOrDefaultAsync(j => j.Name == name, ct);
        return new JobRunState(row?.LastAttemptAt, row?.LastSuccessAt);
    }

    public async Task RecordAsync(JobRunRecord run, CancellationToken ct = default)
    {
        await RecordStateAsync(run, ct);
        await AppendLogAsync(run, ct);
    }

    public async Task RecordStateAsync(JobRunRecord run, CancellationToken ct = default)
    {
        try
        {
            await UpsertAsync(run, ct);
        }
        catch (DbUpdateException)
        {
            // Первую строку задачи одновременно вставили двое (кнопка «Проверить сейчас» и фон):
            // ключ уже занят — перечитываем и обновляем.
            db.ChangeTracker.Clear();
            await UpsertAsync(run, ct);
        }
    }

    public async Task AppendLogAsync(JobRunRecord run, CancellationToken ct = default)
    {
        db.JobRunLog.Add(new JobRunLogEntry
        {
            Name = run.Name,
            Trigger = run.Trigger,
            StartedAt = Utc(run.StartedAt),
            FinishedAt = Utc(run.FinishedAt),
            Success = run.Success,
            Summary = JobRunLog.Clip(run.Summary),
            Error = run.Success ? null : JobRunLog.Clip(run.Error) ?? "ошибка без текста",
            Details = string.IsNullOrWhiteSpace(run.Details) ? null : run.Details,
        });
        await db.SaveChangesAsync(ct);

        // Обрезка: у задачи остаются последние KeepPerJob. Лишних после каждой записи — одна-две
        // строки, поэтому грузим их и удаляем обычным путём (так же работает и в тестах на InMemory).
        var stale = await db.JobRunLog
            .Where(e => e.Name == run.Name)
            .OrderByDescending(e => e.StartedAt).ThenByDescending(e => e.Id)
            .Skip(JobRunLog.KeepPerJob)
            .ToListAsync(ct);
        if (stale.Count > 0)
        {
            db.JobRunLog.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<JobClaimResult> TryClaimAsync(
        string name, string owner, TimeSpan staleAfter, CancellationToken ct = default)
    {
        owner = ClipOwner(owner);
        bool taken;
        if (db.Database.IsNpgsql())
        {
            // Один запрос — одна блокировка строки: из двух процессов заявку получит ровно один.
            // Время — часы базы (now()), а не процесса: у прода и машины Влада они могут расходиться.
            // Первый прогон задачи (строки ещё нет) закрывает INSERT — тоже атомарно, по ключу.
            taken = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Sys_JobRuns" ("Name", "ClaimedAt", "ClaimedBy")
                VALUES ({name}, now(), {owner})
                ON CONFLICT ("Name") DO UPDATE
                    SET "ClaimedAt" = now(), "ClaimedBy" = EXCLUDED."ClaimedBy"
                    WHERE "Sys_JobRuns"."ClaimedAt" IS NULL
                       OR "Sys_JobRuns"."ClaimedAt" < now() - {staleAfter}
                """, ct) > 0;
        }
        else
        {
            // Не Postgres (InMemory в тестах): то же правило обычным путём, без гарантии атомарности.
            var row = await db.JobRuns.FirstOrDefaultAsync(j => j.Name == name, ct);
            if (row is null) db.JobRuns.Add(row = new JobRun { Name = name });
            var now = DateTime.UtcNow;
            taken = row.ClaimedAt is not { } at || now - at >= staleAfter;
            if (taken)
            {
                row.ClaimedAt = now;
                row.ClaimedBy = owner;
                await db.SaveChangesAsync(ct);
            }
            db.ChangeTracker.Clear();
        }

        if (taken) return new JobClaimResult(true);
        var holder = await db.JobRuns.AsNoTracking()
            .Where(j => j.Name == name)
            .Select(j => new { j.ClaimedBy, j.ClaimedAt })
            .FirstOrDefaultAsync(ct);
        return new JobClaimResult(false, holder?.ClaimedBy, holder?.ClaimedAt);
    }

    public async Task ReleaseClaimAsync(string name, string owner, CancellationToken ct = default)
    {
        owner = ClipOwner(owner);
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Sys_JobRuns" SET "ClaimedAt" = NULL, "ClaimedBy" = NULL
                WHERE "Name" = {name} AND "ClaimedBy" = {owner}
                """, ct);
            return;
        }

        var row = await db.JobRuns.FirstOrDefaultAsync(j => j.Name == name && j.ClaimedBy == owner, ct);
        if (row is null) return;
        row.ClaimedAt = null;
        row.ClaimedBy = null;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<JobRunStateRow>> ListStatesAsync(CancellationToken ct = default) =>
        await db.JobRuns.AsNoTracking()
            .OrderBy(j => j.Name)
            .Select(j => new JobRunStateRow(j.Name, j.LastAttemptAt, j.LastSuccessAt, j.LastError, j.ClaimedAt, j.ClaimedBy))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<JobRunLogRow>> ListLogAsync(int perJob, CancellationToken ct = default)
    {
        // Строк в таблице не больше KeepPerJob на задачу, а задач — единицы: читаем всё и режем в памяти.
        var rows = await db.JobRunLog.AsNoTracking()
            .OrderByDescending(e => e.StartedAt).ThenByDescending(e => e.Id)
            .Select(e => new JobRunLogRow(e.Id, e.Name, e.Trigger, e.StartedAt, e.FinishedAt, e.Success, e.Summary, e.Error, e.Details))
            .ToListAsync(ct);
        return rows.GroupBy(r => r.Name).SelectMany(g => g.Take(Math.Max(1, perJob))).ToList();
    }

    public async Task<JobRunStats> GetStatsAsync(DateTime now, CancellationToken ct = default)
    {
        // Один запрос за неделю: строк там не больше KeepPerJob на задачу — считаем в памяти.
        var day = Utc(now).AddDays(-1);
        var week = Utc(now).AddDays(-7);
        var runs = await db.JobRunLog.AsNoTracking()
            .Where(e => e.StartedAt >= week)
            .Select(e => new { e.StartedAt, e.Success })
            .ToListAsync(ct);
        return new JobRunStats(
            runs.Count(r => r.StartedAt >= day), runs.Count(r => r.StartedAt >= day && !r.Success),
            runs.Count, runs.Count(r => !r.Success));
    }

    private async Task UpsertAsync(JobRunRecord run, CancellationToken ct)
    {
        var row = await db.JobRuns.FirstOrDefaultAsync(j => j.Name == run.Name, ct);
        if (row is null)
        {
            row = new JobRun { Name = run.Name };
            db.JobRuns.Add(row);
        }

        var at = Utc(run.StartedAt);
        row.LastAttemptAt = at;
        if (run.Success)
        {
            row.LastSuccessAt = at;
            row.LastError = null;
        }
        else
        {
            row.LastError = JobRunLog.Clip(run.Error) ?? "ошибка без текста";
        }

        await db.SaveChangesAsync(ct);
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string ClipOwner(string owner) =>
        owner.Length > JobSchedule.MaxClaimOwnerLength ? owner[..JobSchedule.MaxClaimOwnerLength] : owner;
}
