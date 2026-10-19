using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Журнал запусков фоновых задач в <c>Sys_JobRuns</c> (В12). Пишет <see cref="SwimmDbContext"/>
/// (рантайм-роль swimm_rw): таблица служебная, публичному пути её не видно.
/// </summary>
public class JobRunStore(SwimmDbContext db) : IJobRunStore
{
    private const int MaxErrorLength = 1000;

    public async Task<JobRunState> GetAsync(string name, CancellationToken ct = default)
    {
        var row = await db.JobRuns.AsNoTracking().FirstOrDefaultAsync(j => j.Name == name, ct);
        return new JobRunState(row?.LastAttemptAt, row?.LastSuccessAt);
    }

    public async Task RecordAsync(string name, DateTime attemptAt, bool success, string? error, CancellationToken ct = default)
    {
        try
        {
            await UpsertAsync(name, attemptAt, success, error, ct);
        }
        catch (DbUpdateException)
        {
            // Первую строку задачи одновременно вставили двое (кнопка «Проверить сейчас» и фон):
            // ключ уже занят — перечитываем и обновляем.
            db.ChangeTracker.Clear();
            await UpsertAsync(name, attemptAt, success, error, ct);
        }
    }

    private async Task UpsertAsync(string name, DateTime attemptAt, bool success, string? error, CancellationToken ct)
    {
        var row = await db.JobRuns.FirstOrDefaultAsync(j => j.Name == name, ct);
        if (row is null)
        {
            row = new JobRun { Name = name };
            db.JobRuns.Add(row);
        }

        var at = DateTime.SpecifyKind(attemptAt, DateTimeKind.Utc);
        row.LastAttemptAt = at;
        if (success)
        {
            row.LastSuccessAt = at;
            row.LastError = null;
        }
        else
        {
            var text = string.IsNullOrWhiteSpace(error) ? "ошибка без текста" : error;
            row.LastError = text.Length > MaxErrorLength ? text[..MaxErrorLength] : text;
        }

        await db.SaveChangesAsync(ct);
    }
}
