using Swimm.Application.Abstractions;

namespace Swimm.Application.Dtos;

/// <summary>Бейдж состояния карточки службы; Variant — success | danger | warning | info | neutral.</summary>
public sealed record JobBadge(string Text, string Variant);

/// <summary>Строка «подпись — значение»; Variant (success/danger/warning/info/neutral) красит значение.</summary>
public sealed record JobFact(string Label, string Value, string? Variant = null);

public sealed record JobLink(string Url, string Text);

/// <summary>
/// Карточка фоновой службы на /Admin/Jobs (docs/admin-pages/jobs.md).
/// </summary>
/// <param name="Kind">schedule | queue | service (<see cref="JobKinds"/>).</param>
/// <param name="History">История прогонов (только у расписаний и только в полном отчёте).</param>
public sealed record JobCard(
    string Key,
    string Title,
    string Kind,
    string Service,
    string What,
    JobBadge Badge,
    IReadOnlyList<JobFact> Facts,
    IReadOnlyList<JobRunLogRow> History,
    JobLink? Link)
{
    /// <summary>Работает в этом процессе: запущена (флаги процесса) и не выключена настройкой.</summary>
    public bool Active { get; init; }

    /// <summary>Расписание: последняя попытка упала.</summary>
    public bool Failed { get; init; }

    /// <summary>Расписание: когда станет пора (UTC); null — пора сразу или не запускается.</summary>
    public DateTime? NextDueAt { get; init; }

    /// <summary>Короткий статус одной строкой — список служб (попап на /Admin/Jobs).</summary>
    public string Status { get; init; } = "";

    /// <summary>Проблема службы: danger | warning; null — всё в порядке.</summary>
    public string? Alert { get; init; }
}

public static class JobKinds
{
    public const string Schedule = "schedule";
    public const string Queue = "queue";
    public const string Service = "service";

    public static string Title(string kind) => kind switch
    {
        Schedule => "расписание",
        Queue => "очередь",
        Service => "служебная",
        _ => kind,
    };
}

/// <summary>Плитка сводки. Tone: ok | warning | danger | info | neutral; Href — якорь карточки или группы.</summary>
public sealed record OverviewTile(string Key, string Label, string Value, string Sub, string Tone, string Href);

/// <summary>Служба с проблемой: что не так и насколько (danger | warning).</summary>
public sealed record JobProblem(string Key, string Title, string Text, string Tone);

/// <summary>
/// Сводка фоновых служб — плитки вверху /Admin/Jobs и блок «Jobs» дашборда. Считает
/// <c>JobsOverviewBuilder.Build</c>, один раз для обоих экранов.
/// </summary>
/// <param name="Tone">Худшее из проблем: danger | warning | ok.</param>
public sealed record JobsSummary(
    int Total,
    int Active,
    IReadOnlyList<JobProblem> Problems,
    int FailedSchedules,
    int OverdueSchedules,
    JobRunStats Stats,
    string? NextTitle,
    DateTime? NextAt,
    string Tone,
    IReadOnlyList<OverviewTile> Tiles);

/// <summary>Состояние процесса и служебных служб, которое сводка берёт не из карточек.</summary>
public sealed record JobsInfraState(
    bool AdminEnabled,
    bool IsDevelopment,
    int Queued,
    int InProgress,
    bool? DbOk,
    DateTime? DbPingAt,
    DataSyncState? Sync,
    CacheBridgeState Bridge);

/// <summary>Полный отчёт для /Admin/Jobs: карточки трёх групп, сводка и последние импорты.</summary>
public sealed record BackgroundJobsReport(
    string EnvironmentName,
    bool AdminEnabled,
    bool BackgroundJobsEnabled,
    IReadOnlyList<JobCard> Schedules,
    IReadOnlyList<JobCard> Queues,
    IReadOnlyList<JobCard> Services,
    JobsSummary Summary,
    IReadOnlyList<ImportHistoryDto> RecentImports)
{
    public IEnumerable<JobCard> All => Schedules.Concat(Queues).Concat(Services);
}
