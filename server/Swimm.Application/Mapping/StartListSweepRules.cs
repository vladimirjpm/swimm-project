using Swimm.Application.Constants;
using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>Что обход стартовых протоколов решил про один старт в этот тик.</summary>
public enum StartListSweepVerdict
{
    /// <summary>Пора — тянем протокол.</summary>
    Due,

    /// <summary>Смотрели недавно, по правилу частоты ещё рано.</summary>
    TooEarly,

    /// <summary>День старта, первый заплыв уже начался — больше не трогаем.</summary>
    Started,

    /// <summary>Старт прошёл (или за окном) — не наше дело.</summary>
    OutOfWindow,

    /// <summary>
    /// loglig-id нет, а старт не ближайший: тянуть нечем, loglig-id дочитает суточная догрузка
    /// деталей (<see cref="StartListSweepRules.DetailsDue"/>).
    /// </summary>
    WaitingLogligId,
}

/// <summary>
/// Один будущий старт глазами обхода: когда начинается, есть ли loglig-id, что лежит в журнале
/// заборов <c>Sys_StartListPulls</c> и (для дня старта) когда первый заплыв.
/// </summary>
/// <param name="DateStart">Первый день старта из «Входящих» — календарная дата (полночь, время не важно).</param>
/// <param name="Pulls">Заборы этого старта из журнала, в любом порядке (можно пусто — ни разу не тянули).</param>
/// <param name="FirstHeatAt">
/// Момент старта первого заплыва в день старта (UTC, <c>CompetitionEntries.HeatStartAt</c>); null —
/// посева нет или время не назначено.
/// </param>
public sealed record StartListSweepCandidate(
    int OrgCompId,
    DateTime DateStart,
    bool HasLogligId,
    IReadOnlyList<StartListPullFact> Pulls,
    DateTime? FirstHeatAt = null);

/// <summary>
/// П3 (docs/plans/prod-auto-pull-plan.md §4.3, решение Р-4): кого из будущих стартов обход
/// стартовых протоколов смотрит в этот тик. Чистая функция — время, журнал заборов и время
/// первого заплыва приходят снаружи, поэтому правило тестируется без сети и базы.
///
/// Посев выкладывают за 12–36 ч до старта, и суточный шаг на всё окно опаздывал на сутки.
/// Теперь обход тикает часто (<c>StartListIntervalHours</c>, 3 ч), а частоту для каждого старта
/// решает таблица:
///
/// | До старта (дни, по Израилю)  | Как часто                                   |
/// |------------------------------|---------------------------------------------|
/// | 3 … окно                     | раз в сутки (программа, loglig-id)          |
/// | 1–2, посева ещё нет          | каждые 3 ч                                  |
/// | 1–2, посев уже забран        | каждые 6 ч (снятия, перестановки)           |
/// | день старта                  | каждые 3 ч до первого заплыва, потом не трогать |
///
/// «Посев уже забран» — в журнале есть забор ok/partial хотя бы с одной заявкой. Правило В4 в
/// силе: заявки без посева не тянем (это делает сам забор, сюда не касается).
/// </summary>
public static class StartListSweepRules
{
    /// <summary>«Ближайшие» старты — до стольких дней (включительно) до старта.</summary>
    public const int NearDays = 2;

    public static readonly TimeSpan FarInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan NearNoSeedInterval = TimeSpan.FromHours(3);
    public static readonly TimeSpan NearSeededInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan StartDayInterval = TimeSpan.FromHours(3);

    /// <summary>
    /// Неудачный последний забор — повтор не позже, чем через столько (даже у дальних): сеть
    /// легла на минуту, ждать из-за этого сутки незачем. Не чаще шага ближайших.
    /// </summary>
    public static readonly TimeSpan RetryAfterError = TimeSpan.FromHours(3);

    /// <summary>
    /// Допуск к интервалу. Тик идёт раз в 3 ч, а забор старта случается не в начале тика, а через
    /// минуты (заборы идут друг за другом). Без допуска старт, забранный в 10:20, на тике в 13:05
    /// был бы «ещё рано» (2 ч 45 мин &lt; 3 ч) и уехал на 16:00. Час с запасом покрывает длину тика;
    /// меньше шага, поэтому два забора одного старта в одном тике не случаются.
    /// </summary>
    public static readonly TimeSpan Slack = TimeSpan.FromHours(1);

    /// <summary>
    /// Сегодняшняя дата по Израилю: «день старта» — по местному календарю, не по UTC (с 00:00 до
    /// 02:00/03:00 по Израилю UTC-дата ещё вчерашняя). Отдаётся в той же форме, что
    /// <c>DateStart</c> «Входящих», — полночь с <see cref="DateTimeKind.Utc"/>: колонка timestamptz,
    /// и Npgsql не пишет в неё дату с Kind=Unspecified.
    /// </summary>
    public static DateTime IsraelToday(DateTime nowUtc) =>
        DateTime.SpecifyKind(IsraelTime.ToLocal(nowUtc).Date, DateTimeKind.Utc);

    /// <summary>Сколько календарных дней (по Израилю) до первого дня старта; 0 — сегодня, &lt; 0 — прошёл.</summary>
    public static int DaysToStart(DateTime dateStart, DateTime nowUtc) =>
        (dateStart.Date - IsraelToday(nowUtc)).Days;

    /// <summary>Посев уже забран: в журнале есть забор ok/partial хотя бы с одной заявкой.</summary>
    public static bool IsSeeded(IEnumerable<StartListPullFact> pulls) =>
        pulls.Any(p => p.Entries > 0 && p.Status is StartListPullStatus.Ok or StartListPullStatus.Partial);

    /// <summary>
    /// Шаг для старта по таблице (без допуска). null — старт не смотрим вовсе (прошёл, за окном,
    /// уже начался).
    /// </summary>
    public static TimeSpan? Interval(StartListSweepCandidate c, DateTime nowUtc, int daysAhead)
    {
        var days = DaysToStart(c.DateStart, nowUtc);
        if (days < 0 || days > Math.Max(1, daysAhead)) return null;
        if (days == 0) return HasStarted(c, nowUtc) ? null : StartDayInterval;
        if (days <= NearDays) return IsSeeded(c.Pulls) ? NearSeededInterval : NearNoSeedInterval;
        return FarInterval;
    }

    /// <summary>Решение по одному старту в момент <paramref name="nowUtc"/>.</summary>
    public static StartListSweepVerdict Decide(StartListSweepCandidate c, DateTime nowUtc, int daysAhead)
    {
        var days = DaysToStart(c.DateStart, nowUtc);
        if (days < 0 || days > Math.Max(1, daysAhead)) return StartListSweepVerdict.OutOfWindow;
        if (days == 0 && HasStarted(c, nowUtc)) return StartListSweepVerdict.Started;

        // Дальнему старту без loglig-id тянуть нечем — его дочитает суточная догрузка деталей.
        // Ближайшему — тянем: забор сам дочитает comp.asp (И-54) и запишет в журнал «пусто»,
        // а журнал задаст ему 3-часовой шаг. Иначе loglig-id ближайших ждал бы суток.
        if (!c.HasLogligId && days > NearDays) return StartListSweepVerdict.WaitingLogligId;

        var interval = Interval(c, nowUtc, daysAhead)!.Value;
        var last = c.Pulls.MaxBy(p => p.PulledAt);
        if (last is null) return StartListSweepVerdict.Due;

        if (last.Status == StartListPullStatus.Error && RetryAfterError < interval)
            interval = RetryAfterError;

        return nowUtc - last.PulledAt >= interval - Slack
            ? StartListSweepVerdict.Due
            : StartListSweepVerdict.TooEarly;
    }

    /// <summary>
    /// День старта и первый заплыв уже начался — больше не трогаем: программа на месте, а что
    /// происходит в бассейне, покажут результаты. Время первого заплыва не на день старта (Р86:
    /// у 16853 два заплыва стояли на чужую дату) — не в счёт, как «не назначено».
    /// </summary>
    public static bool HasStarted(StartListSweepCandidate c, DateTime nowUtc) =>
        c.FirstHeatAt is { } first
        && IsraelTime.ToLocal(first).Date == c.DateStart.Date
        && nowUtc >= first;

    /// <summary>
    /// Суточная часть обхода — догрузка loglig-id всем стартам окна (и недавно прошедшим, И-54): раз
    /// в календарный день по Израилю. Первый тик нового дня (или самый первый прогон) — пора.
    /// </summary>
    /// <param name="previousRunAt">Начало прошлого УСПЕШНОГО прогона задачи (журнал <c>Sys_JobRuns</c>); null — не было.</param>
    public static bool DetailsDue(DateTime? previousRunAt, DateTime nowUtc) =>
        previousRunAt is not { } prev || IsraelToday(prev) < IsraelToday(nowUtc);
}
