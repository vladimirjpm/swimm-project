using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>Имена фоновых задач по расписанию — ключи строк <c>Sys_JobRuns</c>.</summary>
public static class JobNames
{
    /// <summary>Список стартов isr.org.il → «Входящие» (<c>CompetitionDiscoveryBackgroundService</c>).</summary>
    public const string Discovery = "discovery";

    /// <summary>Проход стартового протокола будущих стартов (тот же сервис, второй проход).</summary>
    public const string StartList = "start-list";

    /// <summary>Ночная сверка предложенных loglig-id.</summary>
    public const string LogligVerify = "loglig-verify";

    /// <summary>Пакетная привязка loglig-id.</summary>
    public const string LogligBatch = "loglig-batch";

    /// <summary>Все задачи по расписанию в порядке показа на /Admin/Jobs.</summary>
    public static readonly IReadOnlyList<JobDefinition> All =
    [
        new(Discovery, "DiscoveryEnabled", false, "DiscoveryIntervalHours", 24),
        // Интервал общий с discovery (С10, решение 5: отдельного не заводим).
        new(StartList, "StartListEnabled", false, "DiscoveryIntervalHours", 24),
        // Включена по умолчанию: без неё предложения пользователей зависали бы в Suggested навсегда.
        new(LogligVerify, "LogligVerifyEnabled", true, "LogligVerifyIntervalHours", 24),
        // Выключена по умолчанию: каждый пловец — 1–3 платных запроса Serper.
        new(LogligBatch, "LogligBatchEnabled", false, "LogligBatchIntervalHours", 24),
    ];

    public static JobDefinition Get(string name) => All.First(j => j.Name == name);
}

/// <summary>
/// Кто запустил прогон (колонка <c>Trigger</c> в <c>Sys_JobRunLog</c>). Строки, а не enum:
/// читаются прямо из SQL и стоят в CHECK <c>CK_Sys_JobRunLog_Trigger</c>.
/// </summary>
public static class JobTriggers
{
    /// <summary>Фоновое расписание (<c>ScheduledJob</c>).</summary>
    public const string Schedule = "schedule";

    /// <summary>Кнопка в админке.</summary>
    public const string Manual = "manual";

    /// <summary>Консольная команда (<c>--discovery-sync</c>).</summary>
    public const string Cli = "cli";
}

/// <summary>
/// Настройки задачи по расписанию (Admin/Settings): включена ли и раз во сколько часов. Одно
/// место и для фонового сервиса, и для страницы /Admin/Jobs — иначе умолчания разъедутся.
/// </summary>
public sealed record JobDefinition(
    string Name, string EnabledKey, bool EnabledDefault, string IntervalKey, int DefaultIntervalHours)
{
    public bool IsEnabled(ISettingsService settings) => settings.GetValue(EnabledKey, EnabledDefault);

    /// <summary>Интервал между успешными прогонами; не меньше часа.</summary>
    public TimeSpan Interval(ISettingsService settings) =>
        TimeSpan.FromHours(Math.Max(1, settings.GetValue(IntervalKey, DefaultIntervalHours)));
}

/// <summary>История прогонов (<c>Sys_JobRunLog</c>): сколько строк держим на задачу.</summary>
public static class JobRunLog
{
    /// <summary>
    /// Последние прогоны каждой задачи; старше — удаляются при записи. 50 суточных прогонов —
    /// почти два месяца.
    /// </summary>
    public const int KeepPerJob = 50;

    /// <summary>Длина итога и ошибки в строке истории (колонки varchar(1000)).</summary>
    public const int MaxTextLength = 1000;

    /// <summary>Обрезать текст под колонку; пустой — null.</summary>
    public static string? Clip(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null
        : text.Length > MaxTextLength ? text[..MaxTextLength] : text;
}

/// <summary>
/// Итог прогона одной строкой — то, что видно в истории на /Admin/Jobs. Один текст на задачу,
/// кто бы её ни запустил (фон, кнопка, CLI).
/// </summary>
public static class JobRunSummaries
{
    public static string Discovery(DiscoverySyncResult r) =>
        $"на сайте {r.TotalOnSite}, новых {r.Added}, обновлено {r.Updated}, перенесено {r.Postponed}, пропало {r.Missing}";

    public static string StartList(StartListSweepReport r, int daysAhead) =>
        $"окно {daysAhead} дн.: loglig-id добыт у {r.DetailsResolved} из {r.DetailsChecked}, " +
        $"протоколов забрано {r.Pulled} из {r.Total}";

    public static string LogligVerify(LogligSuggestionVerifyReport r) =>
        r.Checked == 0
            ? "предложений нет"
            : $"проверено {r.Checked}, подтверждено {r.Verified}, отклонено {r.Rejected}, отложено {r.Skipped}";

    public static string LogligBatch(LogligBatchReport r, int perRun) =>
        $"лимит {perRun}: обработано {r.Processed}, привязано {r.Linked}, с кандидатами {r.WithCandidates}, впустую {r.NothingFound}";
}

/// <summary>
/// «Пора ли запускать задачу» (решение В12, docs/plans/competitions-home-plan.md, часть В). Чистая
/// функция: время и журнал приходят снаружи (<c>IJobRunStore</c>), поэтому перезапуск процесса
/// задачу заново не запускает — раньше «последний запуск» был локальной переменной
/// (<see cref="DateTime.MinValue"/>), и каждый рестарт API сразу шёл наружу.
/// </summary>
public static class JobSchedule
{
    /// <summary>После неудачной попытки — не чаще раза в час: сайт лёг, долбить его каждые 5 минут незачем.</summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);

    /// <summary>
    /// Никогда не запускалась → пора. Последняя попытка неудачна (позже последнего успеха или
    /// успеха не было) → пора, когда с неё прошло <paramref name="retryAfterFailure"/>. Иначе —
    /// когда с последнего успеха прошло <paramref name="interval"/>.
    /// </summary>
    public static bool IsDue(
        DateTime now, DateTime? lastSuccess, DateTime? lastAttempt, TimeSpan interval, TimeSpan retryAfterFailure)
    {
        if (lastSuccess is null && lastAttempt is null) return true;

        var failed = lastAttempt is DateTime attempt && (lastSuccess is null || attempt > lastSuccess.Value);
        if (failed) return now - lastAttempt!.Value >= retryAfterFailure;

        return now - lastSuccess!.Value >= interval;
    }

    /// <summary>
    /// Когда задаче станет пора — по тому же правилу, что <see cref="IsDue"/>. null — пора сразу
    /// (не запускалась). Фон смотрит журнал раз в несколько минут, так что реальный запуск —
    /// ближайший тик после этого момента.
    /// </summary>
    public static DateTime? NextDueAt(
        DateTime? lastSuccess, DateTime? lastAttempt, TimeSpan interval, TimeSpan retryAfterFailure)
    {
        if (lastSuccess is null && lastAttempt is null) return null;

        var failed = lastAttempt is DateTime attempt && (lastSuccess is null || attempt > lastSuccess.Value);
        return failed ? lastAttempt!.Value + retryAfterFailure : lastSuccess!.Value + interval;
    }
}
