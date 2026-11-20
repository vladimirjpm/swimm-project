using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>
/// «Ждёт тебя» у results-auto (П5) — ОДНО правило для блока «Соревнования» дашборда
/// (<c>competitions.autoPullBlocked24h</c>) и счётчика «ждёт тебя» мониторинга /Admin/Jobs: строка
/// «Входящих» проверена за последние сутки, вердикт <see cref="ResultsAutoVerdicts.Blocked"/>, ещё
/// новая и не связана с соревнованием в базе (затянутая руками больше никого не ждёт). Тот же
/// предикат, что фильтр <see cref="AutoPullFilters.Blocked"/> на /Admin/Competitions, кроме окна суток.
/// </summary>
public static class ResultsAutoWaiting
{
    /// <summary>Окно «за сутки» по времени проверки (<c>AutoPullCheckedAt</c>).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public static DateTime Since(DateTime now) => now - Window;

    /// <param name="alreadyInDb">Строка связана с соревнованием в базе (матч имя+дата, <c>DiscoveryCompetitionMatcher</c>).</param>
    public static bool IsWaiting(string? verdict, DateTime? checkedAt, string status, bool alreadyInDb, DateTime now) =>
        verdict == ResultsAutoVerdicts.Blocked
        && checkedAt is { } at && at >= Since(now)
        && status == DiscoveredCompetitionStatus.New
        && !alreadyInDb;
}
