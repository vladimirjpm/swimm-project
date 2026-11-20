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
    /// Только состояние задачи, без строки истории: частый тик, который чаще всего ничего не
    /// делает (напоминания — раз в 5 минут), не должен вытеснять историю пустыми прогонами.
    /// </summary>
    Task RecordStateAsync(JobRunRecord run, CancellationToken ct = default);

    /// <summary>
    /// Только строка истории, состояние задачи не трогается: ручной прогон, который не должен
    /// сдвигать расписание (упавшая кнопка, прошлый сезон вместо текущего).
    /// </summary>
    Task AppendLogAsync(JobRunRecord run, CancellationToken ct = default);

    /// <summary>
    /// Взять заявку на прогон задачи (замок, П1 docs/plans/prod-auto-pull-plan.md §4.1) — ОДНИМ
    /// атомарным запросом: получает её ровно один из процессов, смотрящих в одну базу. Заявка
    /// свободна, если её нет или она старше <paramref name="staleAfter"/> (процесс убили посреди
    /// работы). Не взяли — в ответе, кто и с какого времени работает.
    /// </summary>
    Task<JobClaimResult> TryClaimAsync(string name, string owner, TimeSpan staleAfter, CancellationToken ct = default);

    /// <summary>
    /// Снять СВОЮ заявку. Чужую (нашу перехватили как протухшую) не трогает — поэтому сравнение
    /// по <paramref name="owner"/>.
    /// </summary>
    Task ReleaseClaimAsync(string name, string owner, CancellationToken ct = default);

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

/// <summary>
/// Строка <c>Sys_JobRuns</c> для страницы (UTC). <paramref name="ClaimedAt"/> /
/// <paramref name="ClaimedBy"/> — заявка на прогон: задача идёт прямо сейчас (или процесс убит и
/// заявка протухает — <c>JobSchedule.IsClaimLive</c>).
/// </summary>
public sealed record JobRunStateRow(
    string Name, DateTime? LastAttemptAt, DateTime? LastSuccessAt, string? LastError,
    DateTime? ClaimedAt = null, string? ClaimedBy = null);

/// <summary>
/// Итог попытки взять заявку. <paramref name="Taken"/> = false — работает другой:
/// <paramref name="HeldBy"/> с <paramref name="HeldSince"/> (оба null, если он успел закончить,
/// пока мы читали строку).
/// </summary>
public sealed record JobClaimResult(bool Taken, string? HeldBy = null, DateTime? HeldSince = null);

/// <summary>
/// Один прогон задачи (UTC). <paramref name="Trigger"/> — <c>JobTriggers</c> (schedule/manual/cli),
/// <paramref name="Summary"/> — что сделал, одной строкой (<c>JobRunSummaries</c>),
/// <paramref name="Details"/> — подробности JSON-ом (<c>JobRunDetails</c>; пока только discovery).
/// </summary>
public sealed record JobRunRecord(
    string Name,
    string Trigger,
    DateTime StartedAt,
    DateTime FinishedAt,
    bool Success,
    string? Summary,
    string? Error,
    string? Details = null);

/// <summary>
/// Подпись процесса, который пишет журнал: «среда@машина#pid». Одна на процесс (singleton в
/// Program.cs); хранилище ставит её в <c>Sys_JobRunLog.RunBy</c> у каждой строки истории — так
/// видно, шёл прогон с прода, из ProdAdmin или локально. Та же строка — владелец заявки П1.
/// </summary>
public sealed record JobRunOrigin(string Value);

/// <summary>
/// Строка истории прогонов (UTC). <paramref name="Details"/> — JSON <c>JobRunDetails</c> или null;
/// <paramref name="RunBy"/> — откуда шёл прогон (<see cref="JobRunOrigin"/>), null у старых строк.
/// </summary>
public sealed record JobRunLogRow(
    long Id,
    string Name,
    string Trigger,
    DateTime StartedAt,
    DateTime FinishedAt,
    bool Success,
    string? Summary,
    string? Error,
    string? Details = null,
    string? RunBy = null);
