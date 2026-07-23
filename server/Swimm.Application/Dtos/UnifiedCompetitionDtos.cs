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
    IReadOnlyList<int> MonthCounts);

/// <summary>Discovery-сторона объединённой строки: данные с isr.org.il.</summary>
public sealed class UnifiedSiteInfo
{
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
}
