namespace Swimm.Domain;

/// <summary>
/// Календарь сезона — единственный источник правды о том, что такое «сезон» в проекте.
///
/// Сезон = <b>1 сентября — 31 августа</b>, метка по году НАЧАЛА: дата 15/02/2026 лежит в
/// сезоне <c>2025</c> с меткой «2025/26».
///
/// ⚠ В проекте исторически живут ДВЕ разные нумерации сезона, и путать их нельзя:
/// <list type="bullet">
/// <item><b>Год начала</b> (этот класс, <see cref="StartYearOf"/>) — публичная сторона:
/// страницы спортсмена и клуба, «My swims», шапка hub-группы.</item>
/// <item><b>Год федерации</b> (<see cref="FederationYearOf"/>, он же <c>cYear</c> на
/// isr.org.il) — админка и импорт: тот же интервал, но помеченный годом ОКОНЧАНИЯ
/// (окт-2024 … авг-2025 = <c>cYear=2025</c>). Отличается от года начала ровно на 1.</item>
/// </list>
/// Обе нумерации законны; ошибка — молча смешать их в одном месте, получив сдвиг на год.
/// </summary>
public static class SeasonMath
{
    /// <summary>Месяц, с которого начинается сезон (сентябрь).</summary>
    public const int SeasonStartMonth = 9;

    /// <summary>Год начала сезона, которому принадлежит дата. 01/09/2025 → 2025, 31/08/2025 → 2024.</summary>
    public static int StartYearOf(DateTime date) =>
        date.Month >= SeasonStartMonth ? date.Year : date.Year - 1;

    /// <summary>
    /// Год сезона в нумерации федерации (<c>cYear</c> на isr.org.il) — год ОКОНЧАНИЯ.
    /// Ровно <see cref="StartYearOf"/> + 1; отдельный метод, чтобы «+1» не расползался по коду.
    /// </summary>
    public static int FederationYearOf(DateTime date) => StartYearOf(date) + 1;

    /// <summary>Год начала сезона, идущего прямо сейчас (UTC).</summary>
    public static int CurrentStartYear() => StartYearOf(DateTime.UtcNow);

    /// <summary>Метка сезона для UI: 2025 → «2025/26».</summary>
    public static string Label(int startYear) => $"{startYear}/{(startYear + 1) % 100:D2}";

    /// <summary>
    /// Начало сезона (включительно). <see cref="DateTimeKind.Unspecified"/> обязателен:
    /// <c>ResultRecord.CompetitionDate</c> — это <c>timestamp WITHOUT time zone</c>, и Npgsql
    /// отвергает Utc/Local-значения в сравнениях.
    /// </summary>
    public static DateTime StartOf(int startYear) =>
        new(startYear, SeasonStartMonth, 1, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>Конец сезона — начало следующего, сравнивать строго меньше (<c>&lt;</c>).</summary>
    public static DateTime EndExclusiveOf(int startYear) => StartOf(startYear + 1);

    /// <summary>Полуинтервал сезона <c>[Start, EndExclusive)</c> — как есть для предиката EF.</summary>
    public static (DateTime Start, DateTime EndExclusive) RangeOf(int startYear) =>
        (StartOf(startYear), EndExclusiveOf(startYear));

    /// <summary>Дата принадлежит сезону <paramref name="startYear"/>.</summary>
    public static bool IsInSeason(DateTime date, int startYear) => StartYearOf(date) == startYear;
}
