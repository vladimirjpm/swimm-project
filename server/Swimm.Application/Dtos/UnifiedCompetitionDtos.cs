namespace Swimm.Application.Dtos;

/// <summary>
/// Стадия жизненного цикла соревнования в объединённом списке (Competitions + Discovery).
/// Соревнование — одна реальная сущность, живущая в двух источниках: список isr.org.il
/// (<c>Sys_DiscoveredCompetitions</c>) и наша БД (<c>Competitions</c>), связка — OrgCompId.
/// </summary>
public enum CompetitionStage
{
    /// <summary>Есть на сайте (Discovery), в БД ещё нет — ждёт «Затянуть»/импорт.</summary>
    OnSite,
    /// <summary>Есть и на сайте, и в БД, связано по OrgCompId (или имя+дата).</summary>
    Imported,
    /// <summary>Есть только в БД (PDF-импорт без OrgCompId — напр. Маккабиада).</summary>
    DbOnly,
    /// <summary>Discovery-строка скрыта (ignored), в БД её нет.</summary>
    Ignored
}

/// <summary>Результат объединённого списка: страница строк + счётчики по месяцам (для фильтр-кнопок).</summary>
public sealed record UnifiedCompetitionList(
    PagedResult<UnifiedCompetitionRowDto> Page,
    /// <summary>Число соревнований в каждом месяце (индекс 0 = январь … 11 = декабрь) под текущими
    /// фильтрами, но БЕЗ фильтра по месяцу — чтобы на кнопках были полные счётчики.</summary>
    IReadOnlyList<int> MonthCounts,
    /// <summary>Сколько строк спрятал фильтр дисциплины (артистическое плавание и прочее).
    /// Показывается чипом «скрыто N»: молча пропавшие строки читаются как потеря данных.</summary>
    int HiddenByDiscipline = 0,
    /// <summary>Из <see cref="MonthCounts"/> — сколько уже затянуто в БД (строка есть в справочнике).</summary>
    IReadOnlyList<int>? MonthImported = null,
    /// <summary>Счётчики по сезонам под текущими фильтрами, но БЕЗ фильтров сезона и месяца —
    /// чипы-сезоны над списком (сезон, всего, затянуто). Порядок — от свежего к старому.</summary>
    IReadOnlyList<SeasonCountDto>? SeasonCounts = null);

/// <summary>Сезон (год окончания, 2026 = 25/26) со счётчиками «всего / затянуто в БД».</summary>
public sealed record SeasonCountDto(int Season, int Total, int Imported);

/// <summary>Discovery-сторона объединённой строки: данные с isr.org.il.</summary>
public sealed class UnifiedSiteInfo
{
    /// <summary>Вид спорта строки с сайта (проставлен при обнаружении, правится в админке).</summary>
    public string Discipline { get; set; } = "swimming";

    public int DiscoveredId { get; set; }
    public int OrgCompId { get; set; }
    public string Name { get; set; } = "";
    public DateTime DateStart { get; set; }
    public DateTime DateEnd { get; set; }
    public string? Venue { get; set; }
    /// <summary>new | imported | ignored (сырой статус Discovery-строки).</summary>
    public string Status { get; set; } = "";
    /// <summary>Языки загруженных PDF: null | "he" | "en" | "he,en".</summary>
    public string? Languages { get; set; }
    public string? LastError { get; set; }

    /// <summary>Протокола нет: PDF пуст (см. DiscoveredCompetition.EmptySourceAt). Не ошибка —
    /// факт «тянуть нечего», поэтому строка помечается, а не прячется.</summary>
    public DateTime? EmptySourceAt { get; set; }

    public int? LogligId { get; set; }
}

/// <summary>
/// Строка объединённого списка: БД-сторона (соревнование или свёрнутое событие) и/или
/// Discovery-сторона. Хотя бы одна из <see cref="Db"/>/<see cref="Site"/> не null.
/// </summary>
public sealed class UnifiedCompetitionRowDto
{
    public CompetitionStage Stage { get; set; }

    /// <summary>БД-сторона (одиночное соревнование или свёрнутое многодневное событие).
    /// null — строка существует только на сайте (<see cref="CompetitionStage.OnSite"/>/<see cref="CompetitionStage.Ignored"/>).</summary>
    public CompetitionRowDto? Db { get; set; }

    /// <summary>Discovery-сторона. null — соревнование только в БД
    /// (<see cref="CompetitionStage.DbOnly"/>).</summary>
    public UnifiedSiteInfo? Site { get; set; }

    /// <summary>Ключ сортировки по дате (для единого date-desc порядка списка).</summary>
    public DateTime SortDate { get; set; }

    /// <summary>Чемпионат Израиля (по названию любой стороны строки) — иконка 🏆 и фильтр «Тип».</summary>
    public bool IsChampionship { get; set; }

    /// <summary>
    /// Вид спорта строки: swimming | artistic | other. У строки в БД берётся из
    /// <c>Competition.Discipline</c>, у «только на сайте» — из
    /// <c>Sys_DiscoveredCompetitions.Discipline</c>; и то и другое проставлено при
    /// обнаружении/импорте, а не угадывается на каждый показ.
    /// </summary>
    public string Discipline { get; set; } = "swimming";
}
