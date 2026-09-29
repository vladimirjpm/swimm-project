namespace Swimm.Application.Mapping;

/// <summary>
/// Правила флага «On break» (docs/plans/entity-hero-roles-plan.md §5, Ш3.1) — чистые функции,
/// «сегодня» приходит параметром (календарная дата по Израилю).
///
/// Решения Влада 28.09.2026: флаг ставит тренер (бессрочно или до даты), участник — себе, и
/// только ДО даты («травма, отпуск, армия»); после даты человек снова в строю сам. Ответ «Going»
/// снимает перерыв, тренеру — пометка «back after N months». Автоподсказок «не ходит 8 недель»
/// нет. Флаг не отнимает доступа к группе и не трогает публичный состав, зачёт и рекорды.
/// </summary>
public static class HubGroupBreakRules
{
    /// <summary>На сколько дней вперёд можно назначить конец перерыва: дальше — «бессрочно» у тренера.</summary>
    public const int MaxDaysAhead = 366;

    /// <summary>Сколько дней после возвращения тренер видит пометку «back after …».</summary>
    public const int BackMarkerDays = 14;

    /// <summary>
    /// Действует ли перерыв сегодня: не снят и не истёк (<paramref name="until"/> — последний
    /// день перерыва включительно; null — бессрочно).
    /// </summary>
    public static bool IsActive(DateTime? endedAt, DateOnly? until, DateOnly today) =>
        endedAt == null && (until == null || until.Value >= today);

    /// <summary>
    /// Проверка конца перерыва. Себе участник ставит только с датой; тренер может бессрочно.
    /// null — можно, иначе текст отказа (по-английски: его видит пользователь).
    /// </summary>
    public static string? ValidateUntil(DateOnly? until, DateOnly today, bool bySelf)
    {
        if (until == null)
            return bySelf ? "Pick the date you plan to be back." : null;
        if (until.Value < today) return "The break must end today or later.";
        if (until.Value > today.AddDays(MaxDaysAhead)) return $"A break can be set at most {MaxDaysAhead} days ahead.";
        return null;
    }

    /// <summary>
    /// Пометка тренеру «back after N …»: сколько дней человек был на перерыве, если он вернулся
    /// САМ (ответом «Going») не больше <see cref="BackMarkerDays"/> дней назад; иначе null.
    /// </summary>
    public static int? BackAfterDays(DateTime since, DateTime? endedAt, bool endedByRsvp, DateTime nowUtc)
    {
        if (!endedByRsvp || endedAt == null) return null;
        if (nowUtc - endedAt.Value > TimeSpan.FromDays(BackMarkerDays)) return null;
        return Math.Max(0, (int)(endedAt.Value - since).TotalDays);
    }
}
