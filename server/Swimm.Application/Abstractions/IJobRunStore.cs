namespace Swimm.Application.Abstractions;

/// <summary>
/// Журнал запусков фоновых задач по расписанию в базе (<c>Sys_JobRuns</c>, решение В12): «последний
/// запуск» переживает перезапуск процесса. Пора ли запускать — <c>JobSchedule.IsDue</c>, имена
/// задач — <c>JobNames</c>. Плюс история прогонов с итогом (<c>Sys_JobRunLog</c>, /Admin/Jobs):
/// последние <c>JobRunLog.KeepPerJob</c> на задачу.
/// </summary>
public interface IJobRunStore
{
    /// <summary>Последняя попытка и последний успех задачи; обе null — задача ещё не запускалась.</summary>
    Task<JobRunState> GetAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Записать прогон: сдвинуть состояние задачи (от него считается «пора ли») и добавить строку
    /// истории. Успех сдвигает и «последний успех» и стирает ошибку; неудача — только попытку и
    /// текст ошибки (обрезается до 1000).
    /// </summary>
    Task RecordAsync(JobRunRecord run, CancellationToken ct = default);

    /// <summary>
    /// Только строка истории, состояние задачи не трогается: ручной прогон, который не должен
    /// сдвигать расписание (упавшая кнопка, прошлый сезон вместо текущего).
    /// </summary>
    Task AppendLogAsync(JobRunRecord run, CancellationToken ct = default);

    /// <summary>Состояние всех задач, у которых есть строка журнала.</summary>
    Task<IReadOnlyList<JobRunStateRow>> ListStatesAsync(CancellationToken ct = default);

    /// <summary>Последние <paramref name="perJob"/> прогонов каждой задачи, свежие первыми.</summary>
    Task<IReadOnlyList<JobRunLogRow>> ListLogAsync(int perJob, CancellationToken ct = default);

    /// <summary>Сколько прогонов (всех задач) было за сутки и за неделю до <paramref name="now"/> и сколько из них упало.</summary>
    Task<JobRunStats> GetStatsAsync(DateTime now, CancellationToken ct = default);
}

/// <summary>Прогоны всех задач за 24 ч и 7 дней — сводка вверху /Admin/Jobs.</summary>
public sealed record JobRunStats(int Runs24h, int Failed24h, int Runs7d, int Failed7d);

/// <summary>Состояние задачи в журнале (UTC).</summary>
public sealed record JobRunState(DateTime? LastAttemptAt, DateTime? LastSuccessAt);

/// <summary>Строка <c>Sys_JobRuns</c> для страницы (UTC).</summary>
public sealed record JobRunStateRow(string Name, DateTime? LastAttemptAt, DateTime? LastSuccessAt, string? LastError);

/// <summary>
/// Один прогон задачи (UTC). <paramref name="Trigger"/> — <c>JobTriggers</c> (schedule/manual/cli),
/// <paramref name="Summary"/> — что сделал, одной строкой (<c>JobRunSummaries</c>).
/// </summary>
public sealed record JobRunRecord(
    string Name,
    string Trigger,
    DateTime StartedAt,
    DateTime FinishedAt,
    bool Success,
    string? Summary,
    string? Error);

/// <summary>Строка истории прогонов (UTC).</summary>
public sealed record JobRunLogRow(
    long Id,
    string Name,
    string Trigger,
    DateTime StartedAt,
    DateTime FinishedAt,
    bool Success,
    string? Summary,
    string? Error);
