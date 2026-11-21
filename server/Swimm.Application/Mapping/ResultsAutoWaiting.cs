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

    /// <summary>
    /// Через сколько после начала последнего дня «ещё идёт» / «протокола нет» перестаёт быть
    /// ожиданием: полтора суток — день старта кончился, прошло ещё утро следующего.
    /// </summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromHours(36);

    /// <summary>
    /// «Застряло»: вердикт «ещё идёт» или «протокола нет», а старт кончился больше
    /// <see cref="StuckAfter"/> назад. Сама задача такой вердикт не сменит (у 16903 сутки спустя
    /// не хватало двух эстафет из 16 событий, 21.11.2026) и после окна в
    /// <see cref="ResultsAutoRules.WindowDays"/> дней молча перестанет смотреть — значит, это
    /// решение человека: брать как есть, ждать или пометить «нет протокола».
    /// Окно — по дате старта, а не по времени проверки: «протокола нет» перепроверяется редко.
    /// В «Требует внимания» /Admin/Jobs идёт рядом с <see cref="IsWaiting"/>; число дашборда
    /// <c>autoPullBlocked24h</c> и фильтр autopull-blocked — по-прежнему только «ждёт тебя».
    /// </summary>
    public static bool IsStuck(string? verdict, DateTime dateEnd, string status, bool alreadyInDb, DateTime now) =>
        verdict is ResultsAutoVerdicts.NotFinished or ResultsAutoVerdicts.Empty
        && now >= dateEnd + StuckAfter
        && now < dateEnd.AddDays(ResultsAutoRules.WindowDays + 1)
        && status == DiscoveredCompetitionStatus.New
        && !alreadyInDb;
}
