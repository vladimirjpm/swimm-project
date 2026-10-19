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
}
