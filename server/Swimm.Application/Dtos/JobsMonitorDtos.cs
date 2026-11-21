using System.Text.Json;
using Swimm.Application.Mapping;

namespace Swimm.Application.Dtos;

// Мониторинг фоновых служб — GET /api/admin/jobs/monitor (docs/admin-pages/jobs.md, раздел
// «Контракт мониторинга»). Структурные поля вместо готовых строк: страница /Admin/Jobs (виды
// «Таймлайн» и «Стена», панель деталей) и блок Jobs дашборда рисуют всё на клиенте. Собирает
// чистый JobsMonitorBuilder из карточек BackgroundJobsStatusService.

/// <summary>Статус службы для мониторинга — шесть значений (таблица «Статусы» передачи дизайнера).</summary>
public static class JobStatuses
{
    /// <summary>Упала: последний запуск/тик упал, база недоступна, мост не слушает.</summary>
    public const string Failed = "failed";

    /// <summary>Просрочена: пора, а прогона нет; ещё не запускалась; молчит; локальная отстаёт; прод недоступен; ждёт первого пинга.</summary>
    public const string Overdue = "overdue";

    /// <summary>Идёт: живая заявка на прогон или очередь что-то делает.</summary>
    public const string Running = "running";

    /// <summary>В норме.</summary>
    public const string Ok = "ok";

    /// <summary>Очередь ждёт задания (в счётчиках — «в норме»).</summary>
    public const string Idle = "idle";

    /// <summary>Выключена: флагом процесса, настройкой или «не в этом процессе».</summary>
    public const string Off = "off";

    public static readonly IReadOnlyList<string> All = [Failed, Overdue, Running, Ok, Idle, Off];
}

/// <summary>
/// Зона — где выполняется служба. <see cref="Prod"/> — выполняет прод, здесь только наблюдение
/// прод-журнала; <see cref="Local"/> — этот процесс. В Development всегда <see cref="Local"/>.
/// </summary>
public static class JobZones
{
    public const string Prod = "prod";
    public const string Local = "local";
}

/// <summary>Как рисовать дорожку таймлайна.</summary>
public static class JobTimelineModes
{
    /// <summary>Метки прогонов + план.</summary>
    public const string Runs = "runs";

    /// <summary>Частый тик без истории пустых тиков (напоминания): сплошная лента + метки непустых.</summary>
    public const string Ticker = "ticker";

    /// <summary>Нет прогонов во времени (служебные): дорожки нет, плашка.</summary>
    public const string None = "none";
}

/// <summary>Исход прогона в полоске «15 последних» и метках таймлайна.</summary>
public static class JobRunOutcomes
{
    public const string Success = "success";
    public const string Failed = "failed";
    public const string Running = "running";
}

/// <summary>Вид позиции в панели «Требует внимания».</summary>
public static class JobAttentionKinds
{
    public const string Failed = "failed";
    public const string Overdue = "overdue";

    /// <summary>Брошенная заявка: процесс убит посреди прогона, заявка не снята (старше <c>JobSchedule.ClaimStaleAfter</c>).</summary>
    public const string AbandonedClaim = "abandoned-claim";

    /// <summary>results-auto: строка «Входящих» ждёт решения человека.</summary>
    public const string Waiting = "waiting";
}

/// <summary>Откуда шёл прогон — код по подписи «среда@машина#pid» (<c>Sys_JobRunLog.RunBy</c>).</summary>
public static class JobOrigins
{
    public const string Prod = "prod";
    public const string ProdAdmin = "prodadmin";
    public const string Local = "local";
    public const string Other = "other";
}

/// <summary>
/// Шесть счётчиков сводки в порядке показа: упала · просрочена · идут · ждёт тебя · в норме
/// (вместе с «ждёт задания») · выключены. «Ждёт тебя» — не статус службы, а число строк
/// results-auto «ждёт тебя» за сутки (<c>ResultsAutoWaiting</c>).
/// </summary>
public sealed record JobsCounters(int Failed, int Overdue, int Running, int Waiting, int Ok, int Off)
{
    public static readonly JobsCounters Empty = new(0, 0, 0, 0, 0, 0);
}

/// <summary>Выключатель задачи в Admin/Settings. <paramref name="IsDefault"/> — ключа в базе нет, взято умолчание.</summary>
public sealed record JobToggle(string Key, bool Value, bool IsDefault);

/// <summary>Параметр задачи из Admin/Settings (интервал, режим, лимит). Value — как хранится; IsDefault — ключа нет.</summary>
public sealed record JobSettingValue(string Key, string? Value, bool IsDefault);

/// <summary>
/// Заявка на прогон (<c>Sys_JobRuns.ClaimedAt/ClaimedBy</c>, замок П1). <paramref name="Live"/> = false —
/// брошенная (старше <c>JobSchedule.ClaimStaleAfter</c>).
/// </summary>
public sealed record JobClaimInfo(DateTime Since, string? By, bool Live, string? Origin);

/// <summary>Прогресс идущей работы: «120 из 470 стран, сейчас FRA».</summary>
/// <param name="Unit">Чего считаем: «стран», «карточек».</param>
/// <param name="Note">Вторая строка: «строк 3400», «пар 3 из 12».</param>
public sealed record JobProgress(int Done, int Total, string? Current, string Unit, string? Note);

/// <summary>Очередь в памяти процесса: сейчас и с запуска процесса.</summary>
public sealed record JobQueueCounts(int Queued, int Running, int Completed, int Failed);

/// <summary>Ссылка или действие из панели деталей. Kind: <c>link</c> (GET-переход) | <c>post</c> (кнопка, POST с antiforgery).</summary>
/// <param name="Hint">Подсказка: «до 5 минут», «после ответа — обновить».</param>
public sealed record JobAction(string Kind, string Text, string Url, string? Hint = null)
{
    public const string Link = "link";
    public const string Post = "post";
}

/// <summary>Метка прогона на таймлайне. Outcome — <see cref="JobRunOutcomes"/>; у идущего FinishedAt = null.</summary>
public sealed record JobWindowRun(
    long? Id, DateTime StartedAt, DateTime? FinishedAt, string Outcome, string? Trigger, string? Origin, string? RunBy);

/// <summary>
/// Строка истории для панели деталей. Подробности (<c>Details</c>, jsonb) сюда не входят — только
/// признак <paramref name="HasDetails"/>; сами — <c>GET /api/admin/jobs/runs/{id}</c>.
/// </summary>
public sealed record JobHistoryEntry(
    long Id,
    DateTime StartedAt,
    DateTime FinishedAt,
    int DurationSeconds,
    string Trigger,
    string TriggerText,
    string? Origin,
    string? OriginText,
    string? RunBy,
    bool Success,
    string? Summary,
    string? Error,
    bool HasDetails);

/// <summary>
/// Структурные поля карточки, которые служба знает при сборке (<c>JobCard.Monitor</c>). Из них и
/// истории журнала <c>JobsMonitorBuilder</c> собирает <see cref="JobMonitorItem"/>.
/// </summary>
public sealed record JobMonitorExtras
{
    public string Zone { get; init; } = JobZones.Local;
    public string TimelineMode { get; init; } = JobTimelineModes.None;
    public TimeSpan? Interval { get; init; }
    public string? IntervalKey { get; init; }
    public JobToggle? Toggle { get; init; }
    public IReadOnlyList<JobSettingValue> Settings { get; init; } = [];

    /// <summary>Последний запуск/тик/пинг/сверка (UTC).</summary>
    public DateTime? LastRunAt { get; init; }
    public DateTime? LastSuccessAt { get; init; }

    /// <summary>Когда станет пора (UTC) — то же, что <c>JobCard.NextDueAt</c>; у напоминаний — последний тик + 5 мин.</summary>
    public DateTime? NextAt { get; init; }

    /// <summary>Следующий — повтор после ошибки (через <c>JobSchedule.RetryAfterFailure</c>).</summary>
    public bool NextIsRetry { get; init; }

    /// <summary>План приблизителен: у <c>start-list</c> частоту стартов решает правило близости, шаг — только тик.</summary>
    public bool NextApprox { get; init; }

    public JobClaimInfo? Claim { get; init; }

    /// <summary>Полный текст последней ошибки (как хранится, до 1000 символов); null — последняя попытка удалась.</summary>
    public string? LastError { get; init; }

    /// <summary>Итог последней работы — у очередей и служебных (у расписаний берётся из истории журнала).</summary>
    public string? LastSummary { get; init; }

    /// <summary>С какого момента в текущем состоянии: идёт с…, слушает с….</summary>
    public DateTime? StateSince { get; init; }

    public JobProgress? Progress { get; init; }
    public JobQueueCounts? Queue { get; init; }

    /// <summary>Прогоны очереди из памяти процесса (свежие первыми) — у расписаний пусто, они в журнале.</summary>
    public IReadOnlyList<JobWindowRun> QueueRuns { get; init; } = [];

    public IReadOnlyList<JobAction> Actions { get; init; } = [];
}

/// <summary>Одна служба в мониторинге. Поля и значения — docs/admin-pages/jobs.md, «Контракт мониторинга».</summary>
public sealed record JobMonitorItem
{
    public required string Key { get; init; }
    public required string Title { get; init; }

    /// <summary><see cref="JobKinds"/>: schedule | queue | service.</summary>
    public required string Kind { get; init; }

    /// <summary><see cref="JobZones"/>.</summary>
    public required string Zone { get; init; }

    /// <summary><see cref="JobStatuses"/>.</summary>
    public required string Status { get; init; }

    /// <summary>Исходный бейдж нынешней карточки (текст и variant) — для подписи статуса.</summary>
    public required JobBadge Badge { get; init; }

    /// <summary>Короткий статус одной строкой (как в списке служб нынешней страницы).</summary>
    public required string StatusText { get; init; }

    /// <summary>Проблема для главной цифры дашборда: danger | warning | null (как <c>JobCard.Alert</c>).</summary>
    public string? Alert { get; init; }

    /// <summary>Работает (здесь или наблюдается на проде) и не выключена.</summary>
    public bool Active { get; init; }

    public required string What { get; init; }
    public required string Service { get; init; }
    public int? IntervalSeconds { get; init; }
    public string? IntervalKey { get; init; }
    public JobToggle? Toggle { get; init; }
    public IReadOnlyList<JobSettingValue> Settings { get; init; } = [];

    public DateTime? LastRunAt { get; init; }
    public DateTime? LastSuccessAt { get; init; }

    /// <summary>Последняя строка истории (без подробностей); у напоминаний — последний НЕПУСТОЙ тик.</summary>
    public JobHistoryEntry? LastRun { get; init; }
    public int? LastDurationSeconds { get; init; }

    public DateTime? NextAt { get; init; }
    public bool NextIsRetry { get; init; }
    public bool NextApprox { get; init; }

    /// <summary>Пора прямо сейчас — запустится при ближайшей проверке журнала (NextAt в прошлом или ещё не запускалась).</summary>
    public bool DueNow { get; init; }

    /// <summary>Просрочка: с какого момента «пора» и на сколько секунд (только при Status = overdue и известном моменте).</summary>
    public DateTime? OverdueSince { get; init; }
    public int? OverdueSeconds { get; init; }

    public JobClaimInfo? Claim { get; init; }
    public string? LastError { get; init; }
    public string? LastSummary { get; init; }
    public DateTime? StateSince { get; init; }

    /// <summary>До 15 исходов, свежие первыми: <see cref="JobRunOutcomes"/>.</summary>
    public IReadOnlyList<string> RecentOutcomes { get; init; } = [];

    /// <summary><see cref="JobTimelineModes"/>.</summary>
    public required string TimelineMode { get; init; }

    /// <summary>Прогоны, попавшие в окно таймлайна (по возрастанию начала), включая идущий.</summary>
    public IReadOnlyList<JobWindowRun> WindowRuns { get; init; } = [];

    /// <summary>Плановые моменты после «сейчас» до конца окна (UTC); при NextApprox — ориентир.</summary>
    public IReadOnlyList<DateTime> Planned { get; init; } = [];

    public JobProgress? Progress { get; init; }
    public JobQueueCounts? Queue { get; init; }

    /// <summary>Пары «подпись–значение» для панели деталей (как факты нынешней карточки).</summary>
    public IReadOnlyList<JobFact> Facts { get; init; } = [];
    public IReadOnlyList<JobAction> Actions { get; init; } = [];

    /// <summary>Последние прогоны (до 15, свежие первыми) — таблица в панели деталей.</summary>
    public IReadOnlyList<JobHistoryEntry> History { get; init; } = [];
}

/// <summary>
/// Зона и её «пульс». У прода — подпись процесса из последней строки журнала с <c>RunBy</c>
/// «Production@…» и последний тик <c>training-reminders</c> (<paramref name="PulseKind"/> = <c>tick</c>);
/// у local — подпись этого процесса и время его запуска (<c>started</c>).
/// </summary>
public sealed record JobZoneInfo(string Key, string? Process, DateTime? PulseAt, string PulseKind, IReadOnlyList<string> JobKeys);

/// <summary>Позиция «Требует внимания». Href — ссылка наружу (у «ждёт тебя»); у служб null — клиент открывает панель по Key.</summary>
public sealed record JobAttentionItem(
    string Key, string Title, string Zone, string Kind, string Status, string Text, DateTime? Since, string? Href);

/// <summary>Позиция «Идёт сейчас». Progress = null — «без оценки» (полосатая полоса).</summary>
public sealed record JobRunningItem(string Key, string Title, string Zone, DateTime? Since, string? By, JobProgress? Progress);

/// <summary>Позиция «Ближайшие запуски».</summary>
public sealed record JobUpcomingItem(string Key, string Title, string Zone, DateTime At, bool IsRetry, bool Approx);

/// <summary>
/// Строка results-auto «ждёт тебя»: проверена за сутки, вердикт blocked, не затянута
/// (<c>ResultsAutoWaiting</c> — тот же расчёт, что <c>competitions.autoPullBlocked24h</c> дашборда).
/// С 21.11.2026 сюда же идут «застрявшие» (<c>ResultsAutoWaiting.IsStuck</c>): <paramref name="Verdict"/>
/// = <c>not-finished</c> / <c>empty</c>, старт кончился больше полутора суток назад.
/// </summary>
/// <param name="Verdict">Вердикт строки: <c>blocked</c> — «ждёт тебя»; <c>not-finished</c> / <c>empty</c> — застряла.</param>
public sealed record ResultsAutoWaitingRow(
    int Id, int OrgCompId, string Name, DateTime DateStart, DateTime DateEnd, string? Reason, DateTime? CheckedAt,
    string Verdict = "blocked");

/// <summary>Ответ <c>GET /api/admin/jobs/monitor</c> — всё для страницы /Admin/Jobs одним JSON.</summary>
/// <param name="Environment">Среда процесса: ProdAdmin | Development.</param>
/// <param name="Journal">Чей журнал показан: <c>prod</c> (прод-база, ProdAdmin) | <c>local</c>.</param>
/// <param name="Summary">Та же сводка, что <c>GET /api/admin/jobs/summary</c> (<c>JobsOverviewBuilder.Build</c>).</param>
/// <param name="RecordsSources">Плитка «Рекорды · источники» (<c>RecordsCheckTileBuilder</c>).</param>
public sealed record JobsMonitor(
    DateTime Now,
    string Environment,
    string Journal,
    DateTime WindowFrom,
    DateTime WindowTo,
    int WindowHours,
    IReadOnlyList<JobZoneInfo> Zones,
    JobsCounters Counters,
    IReadOnlyList<JobMonitorItem> Jobs,
    IReadOnlyList<JobAttentionItem> Attention,
    IReadOnlyList<JobRunningItem> Running,
    IReadOnlyList<JobUpcomingItem> Upcoming,
    IReadOnlyList<ResultsAutoWaitingRow> Waiting,
    JobsSummary Summary,
    RecordsCheckTile? RecordsSources,
    IReadOnlyList<ImportHistoryDto> RecentImports);

/// <summary>
/// Ответ <c>GET /api/admin/jobs/runs/{id}</c> — один прогон с подробностями. <paramref name="Details"/> —
/// JSON как записан (discovery: <c>DiscoveryRunDetails</c>, results-auto: <c>ResultsAutoTickReport</c>);
/// <paramref name="DiscoveryChanges"/> — готовый вид окна «Что изменилось» (только discovery).
/// </summary>
public sealed record JobRunDetailsView(
    long Id,
    string Name,
    string Trigger,
    DateTime StartedAt,
    DateTime FinishedAt,
    bool Success,
    string? RunBy,
    string? Origin,
    string? Summary,
    string? Error,
    JsonElement? Details,
    DiscoveryRunChangesView? DiscoveryChanges);
