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

    public async Task<IReadOnlyList<JobRunStateRow>> ListStatesAsync(CancellationToken ct = default) =>
        await db.JobRuns.AsNoTracking()
            .OrderBy(j => j.Name)
            .Select(j => new JobRunStateRow(j.Name, j.LastAttemptAt, j.LastSuccessAt, j.LastError))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<JobRunLogRow>> ListLogAsync(int perJob, CancellationToken ct = default)
    {
        // Строк в таблице не больше KeepPerJob на задачу, а задач — единицы: читаем всё и режем в памяти.
        var rows = await db.JobRunLog.AsNoTracking()
            .OrderByDescending(e => e.StartedAt).ThenByDescending(e => e.Id)
            .Select(e => new JobRunLogRow(e.Id, e.Name, e.Trigger, e.StartedAt, e.FinishedAt, e.Success, e.Summary, e.Error))
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
}
