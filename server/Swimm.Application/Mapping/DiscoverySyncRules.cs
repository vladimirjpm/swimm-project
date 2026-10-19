using Swimm.Domain;
using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>Что значит смена даты начала у известной строки «Входящих».</summary>
public enum DiscoveryDateChange
{
    /// <summary>Дата начала не менялась или правили прошедший старт (опечатка) — переноса нет.</summary>
    None,

    /// <summary>Будущий старт сдвинули — перенос: запомнить ПЕРВУЮ объявленную дату.</summary>
    Postponed,

    /// <summary>Старт вернули на первую объявленную дату — перенос отменили: забыть прежнюю.</summary>
    Restored,
}

/// <summary>Строка «Входящих» глазами правила отмены — только то, что ему нужно.</summary>
public sealed record DiscoveryKnownRow(int OrgCompId, DateOnly Start, string Status);

/// <summary>
/// Итог проверки пропавших: кого пометить <c>MissingSince</c>. <see cref="Suppressed"/> — сработал
/// предохранитель (список пуст или пропало подозрительно много), помечать нельзя никого.
/// </summary>
public sealed record DiscoveryMissingDecision(IReadOnlyList<int> Missing, bool Suppressed, string? Reason);

/// <summary>
/// Перенос и отмена старта по синхронизации со списком isr.org.il (шаг В4′, решение В4 —
/// docs/plans/competitions-home-plan.md, часть В). Пометок «отменён» / «перенесён» на сайте
/// федерации нет, поэтому их выводим из того, как меняется сам список: у той же compID сменилась
/// дата — перенос; будущий старт исчез из списка — отмена. Без новых обращений наружу.
/// Чистые функции; применяет их <c>CompetitionDiscoveryService.SyncAsync</c>.
/// </summary>
public static class DiscoverySyncRules
{
    /// <summary>Пропасть может не больше стольких будущих стартов разом (или половины), иначе — сбой сайта.</summary>
    public const int MissingFloor = 3;

    /// <summary>
    /// Смена даты начала. Перенос — только у старта, который ещё не прошёл (прежняя дата ≥
    /// сегодня по Израилю): правка даты прошедшего старта — опечатка на сайте, а не перенос.
    /// Новая дата равна первой объявленной — перенос отменили.
    /// </summary>
    public static DiscoveryDateChange ClassifyDateChange(
        DateOnly oldStart, DateOnly newStart, DateOnly? previousStart, DateOnly today)
    {
        if (oldStart == newStart) return DiscoveryDateChange.None;
        if (oldStart < today) return DiscoveryDateChange.None;
        if (previousStart == newStart) return DiscoveryDateChange.Restored;
        return DiscoveryDateChange.Postponed;
    }

    /// <summary>
    /// Будущие старты текущего сезона (не скрытые админом), которых нет в полученных списках.
    /// Звать только при заборе ТЕКУЩЕГО сезона, когда оба списка (прошедшие и будущие) пришли:
    /// старт, который уже прошёл, переезжает из «будущих» в «прошедшие» и не должен считаться
    /// пропавшим.
    ///
    /// Предохранитель от сбоя сайта: будущий список пуст ИЛИ пропало больше
    /// <c>max(<see cref="MissingFloor"/>, половина будущих строк сезона)</c> — не помечать никого.
    /// </summary>
    public static DiscoveryMissingDecision FindMissing(
        IEnumerable<DiscoveryKnownRow> known, IReadOnlySet<int> seenOnSite, int upcomingOnSite, DateOnly today)
    {
        var season = SeasonMath.StartYearOf(today.ToDateTime(TimeOnly.MinValue));
        var future = known
            .Where(r => r.Status != DiscoveredCompetitionStatus.Ignored
                        && r.Start >= today
                        && SeasonMath.StartYearOf(r.Start.ToDateTime(TimeOnly.MinValue)) == season)
            .ToList();
        var missing = future.Where(r => !seenOnSite.Contains(r.OrgCompId)).Select(r => r.OrgCompId).ToList();

        if (missing.Count == 0) return new DiscoveryMissingDecision([], false, null);

        if (upcomingOnSite == 0)
            return new DiscoveryMissingDecision([], true,
                $"список будущих стартов пуст — {missing.Count} будущих стартов сезона не помечаю отменёнными");

        var limit = Math.Max(MissingFloor, future.Count / 2);
        if (missing.Count > limit)
            return new DiscoveryMissingDecision([], true,
                $"пропало {missing.Count} из {future.Count} будущих стартов сезона (порог {limit}) — похоже на сбой сайта, отмену не помечаю");

        return new DiscoveryMissingDecision(missing, false, null);
    }
}
