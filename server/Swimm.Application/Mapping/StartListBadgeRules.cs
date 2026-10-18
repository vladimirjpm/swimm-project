namespace Swimm.Application.Mapping;

/// <summary>Чем кончился последний забор стартового протокола — цвет бейджа в /Admin/Competitions.</summary>
public enum StartListBadgeKind
{
    /// <summary>Протокол прочитан полностью, заявки есть.</summary>
    Ok,
    /// <summary>Часть заплывов не прочиталась — повторное нажатие дочитает.</summary>
    Partial,
    /// <summary>Тянуть пока нечего: посев на loglig не сделан (или нет loglig-id). Не ошибка.</summary>
    Empty,
    /// <summary>Забор сорвался.</summary>
    Error,
}

/// <summary>
/// Один забор из журнала <c>Sys_StartListPulls</c> — то, что нужно бейджу и подсказке к нему.
/// </summary>
public sealed record StartListPullFact(
    DateTime PulledAt, string Status, int Events, int Entries, int Added, int Removed, int Moved);

/// <summary>
/// Бейдж «стартовый протокол затянут» в строке соревнования. <c>Last</c> — последний забор,
/// <c>History</c> — последние несколько от свежего к старому (подсказка при наведении), <c>Stale</c>
/// — старт вот-вот, а забор давно: пора нажать ещё раз.
/// </summary>
public sealed record StartListBadge(
    StartListBadgeKind Kind, bool Stale, StartListPullFact Last, IReadOnlyList<StartListPullFact> History);

/// <summary>
/// Пометка в /Admin/Competitions, что стартовый протокол уже затягивали (решение Влада 18.10.2026,
/// docs/admin-pages/competitions.md «Бейдж стартового протокола»).
///
/// Отдельной галки «затянуто» сознательно НЕТ: факт уже лежит в журнале заборов
/// <c>Sys_StartListPulls</c>, и второй его экземпляр рано или поздно с журналом разошёлся бы.
/// Бейдж читается из журнала — одно место правды.
///
/// «Пора обновить»: стартовый протокол меняется до последнего дня (снятия, пересев), и
/// пропустить это проще всего. Поэтому, когда до старта <see cref="StaleWindowDays"/> дня или
/// меньше, а последнему забору больше <see cref="StaleAfter"/>, бейдж просит нажать ещё раз.
/// Прошедшим стартам обновлять нечего — у них пометки нет.
/// </summary>
public static class StartListBadgeRules
{
    /// <summary>За сколько дней до старта начинаем следить за свежестью забора.</summary>
    public const int StaleWindowDays = 3;

    /// <summary>Забор старше этого внутри окна — «пора обновить».</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(24);

    /// <summary>Сколько последних заборов показывать в подсказке.</summary>
    public const int HistorySize = 5;

    /// <summary>
    /// Бейдж по журналу заборов одного соревнования. Пустой журнал — null: ни разу не тянули,
    /// бейджа нет. <paramref name="competitionStart"/> — первый день старта (дата, время
    /// не важно); <paramref name="nowUtc"/> — сейчас.
    /// </summary>
    public static StartListBadge? Evaluate(
        IEnumerable<StartListPullFact> pulls, DateTime competitionStart, DateTime nowUtc)
    {
        var history = pulls.OrderByDescending(p => p.PulledAt).Take(HistorySize).ToList();
        if (history.Count == 0) return null;

        var last = history[0];
        return new StartListBadge(KindOf(last), IsStale(last.PulledAt, competitionStart, nowUtc), last, history);
    }

    /// <summary>
    /// Цвет по исходу забора. «ok» без единой заявки — по смыслу то же «пусто»: программа
    /// есть, людей нет. Незнакомый статус — как ошибка: лучше лишний раз посмотреть.
    /// </summary>
    public static StartListBadgeKind KindOf(StartListPullFact pull) => pull.Status switch
    {
        "ok" => pull.Entries > 0 ? StartListBadgeKind.Ok : StartListBadgeKind.Empty,
        "partial" => StartListBadgeKind.Partial,
        "empty" => StartListBadgeKind.Empty,
        _ => StartListBadgeKind.Error,
    };

    /// <summary>
    /// Старт сегодня или в ближайшие <see cref="StaleWindowDays"/> дня, а забор старше
    /// <see cref="StaleAfter"/>. Сравниваем календарные даты: время старта во входящих не
    /// хранится (полночь), а «за три дня» — это про дни, не про часы.
    /// </summary>
    public static bool IsStale(DateTime lastPulledAt, DateTime competitionStart, DateTime nowUtc)
    {
        var daysToStart = (competitionStart.Date - nowUtc.Date).Days;
        if (daysToStart < 0 || daysToStart > StaleWindowDays) return false;
        return nowUtc - lastPulledAt > StaleAfter;
    }
}
