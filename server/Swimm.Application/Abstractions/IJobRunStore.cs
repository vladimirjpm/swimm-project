namespace Swimm.Application.Abstractions;

/// <summary>
/// Журнал запусков фоновых задач по расписанию в базе (<c>Sys_JobRuns</c>, решение В12): «последний
/// запуск» переживает перезапуск процесса. Пора ли запускать — <c>JobSchedule.IsDue</c>, имена
/// задач — <c>JobNames</c>.
/// </summary>
public interface IJobRunStore
{
    /// <summary>Последняя попытка и последний успех задачи; обе null — задача ещё не запускалась.</summary>
    Task<JobRunState> GetAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Записать попытку, начатую в <paramref name="attemptAt"/>. Успех сдвигает и «последний
    /// успех» и стирает ошибку; неудача — только попытку и текст ошибки (обрезается до 1000).
    /// </summary>
    Task RecordAsync(string name, DateTime attemptAt, bool success, string? error, CancellationToken ct = default);
}

/// <summary>Состояние задачи в журнале (UTC).</summary>
public sealed record JobRunState(DateTime? LastAttemptAt, DateTime? LastSuccessAt);
