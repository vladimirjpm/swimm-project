using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Календарь сезона для главной <c>/competitions</c> (docs/plans/competitions-home-plan.md,
/// часть Б, шаг Б1; часть В). Источник — только наша база: справочник <c>Competitions</c>
/// (дни события свёрнуты в одну запись) + предстоящие старты, у которых уже есть стартовый
/// протокол (<c>CompetitionEntries</c>), но ещё нет строки в справочнике, + старты сезона из
/// «Входящих» (<c>Sys_DiscoveredCompetitions</c>, решение В9), которых нет ни там, ни там.
/// Даты — ISO <c>yyyy-MM-dd</c>: календарю нужна арифметика по дням, а не подпись.
/// </summary>
public sealed class CompetitionCalendarDto
{
    /// <summary>Год НАЧАЛА сезона (сентябрь–август): 2026 = «2026/27».</summary>
    [JsonPropertyName("season")]
    public int Season { get; init; }

    [JsonPropertyName("season_label")]
    public string SeasonLabel { get; init; } = string.Empty;

    /// <summary>«Сегодня» по Asia/Jerusalem — клиент обводит этот день и гасит прошедшие.</summary>
    [JsonPropertyName("today")]
    public string Today { get; init; } = string.Empty;

    /// <summary>Сезоны, в которых у нас есть хоть один старт (по возрастанию) — границы листания ‹ ›.
    /// Текущий сезон в списке всегда, даже пустой.</summary>
    [JsonPropertyName("seasons")]
    public IReadOnlyList<int> Seasons { get; init; } = [];

    /// <summary>Старты сезона по дате начала, при равенстве — по названию.</summary>
    [JsonPropertyName("meets")]
    public IReadOnlyList<CalendarMeetDto> Meets { get; init; } = [];
}

/// <summary>Один старт календаря — куб, плашка в ячейке, строка в карточке месяца.</summary>
public sealed class CalendarMeetDto
{
    /// <summary>Уникальный ключ строки: <c>event:12</c> | <c>competition:1766</c> | <c>upcoming:16900</c> |
    /// <c>postponed:16900</c> («призрак» перенесённого старта на прежних датах).</summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    /// <summary>"event" — многодневное событие (ссылка <c>/results?eventId=</c>); "competition" —
    /// однодневное (<c>/competitions/{id}</c>); "upcoming" — не затянут: стартовый протокол и/или строка «Входящих»
    /// (<c>/competitions/upcoming/{orgCompId}</c>).</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = CalendarMeetKinds.Competition;

    /// <summary>EventId | CompetitionId | OrgCompId — смотря по <see cref="Kind"/>.</summary>
    [JsonPropertyName("id")]
    public int Id { get; init; }

    /// <summary>Название на иврите — клиент рисует его с <c>dir="rtl"</c>.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("start")]
    public string Start { get; init; } = string.Empty;

    [JsonPropertyName("end")]
    public string End { get; init; } = string.Empty;

    /// <summary>Дни старта по возрастанию; <c>loaded</c> — у дня есть результаты. Полоса дней
    /// в кубе многодневки («Day 1 · 16 Oct ✓»).</summary>
    [JsonPropertyName("days")]
    public IReadOnlyList<CalendarMeetDayDto> Days { get; init; } = [];

    [JsonPropertyName("days_loaded")]
    public int DaysLoaded { get; init; }

    [JsonPropertyName("days_total")]
    public int DaysTotal { get; init; }

    /// <summary>"25m" | "50m" | "" (не знаем — у предстоящего без справочной строки).</summary>
    [JsonPropertyName("pool")]
    public string Pool { get; init; } = string.Empty;

    /// <summary>Площадка из «Входящих» по compID (решение В5); null — строки входящих нет.</summary>
    [JsonPropertyName("venue")]
    public string? Venue { get; init; }

    /// <summary>"pool" | "open_water" | "artistic" — см. <c>CalendarMeetTypes</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = CalendarMeetTypes.Pool;

    [JsonPropertyName("is_championship")]
    public bool IsChampionship { get; init; }

    /// <summary>Возрастная группа старта; null — не определить (кастомная категория, нет данных).</summary>
    [JsonPropertyName("group")]
    public CalendarGroupDto? Group { get; init; }

    /// <summary>"upcoming" | "live" | "in_progress" | "complete" | "done" | "cancelled" | "postponed".
    /// Последние два — только у kind=upcoming (шаг В4′): cancelled — старт пропал из списка
    /// isr.org.il; postponed — «призрак» на прежних датах перенесённого старта.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = CalendarMeetStatuses.Done;

    /// <summary>Новая дата начала перенесённого старта (ISO) — только у «призрака» postponed.</summary>
    [JsonPropertyName("postponed_to")]
    public string? PostponedTo { get; init; }

    /// <summary>Есть стартовый протокол (заявки в <c>CompetitionEntries</c>).</summary>
    [JsonPropertyName("has_start_list")]
    public bool HasStartList { get; init; }

    /// <summary>Страница старта на сайте федерации (<c>https://isr.org.il/comp.asp?compID=</c>) — только
    /// у kind=upcoming со строкой «Входящих»; клиент ведёт туда <c>Details ↗</c>, если заявок нет.
    /// У затянутых (event/competition) — null.</summary>
    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; init; }
}

public sealed class CalendarMeetDayDto
{
    [JsonPropertyName("date")]
    public string Date { get; init; } = string.Empty;

    [JsonPropertyName("loaded")]
    public bool Loaded { get; init; }
}

/// <summary>Бейдж группы K/Y/J/A/M (решение В6: буква — из категорий, сезон — по названию).</summary>
public sealed class CalendarGroupDto
{
    /// <summary>K | Y | J | A | M.</summary>
    [JsonPropertyName("letter")]
    public string Letter { get; init; } = string.Empty;

    /// <summary>Kids | Young | Juniors | Adults | Masters — подпись в мета-строке куба.</summary>
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    /// <summary>«8–11», «17+»; null — полоса возраста не задана.</summary>
    [JsonPropertyName("ages")]
    public string? Ages { get; init; }

    /// <summary>"winter" (❄) | "summer" (☀) | null.</summary>
    [JsonPropertyName("season")]
    public string? Season { get; init; }
}

public static class CalendarMeetKinds
{
    public const string Event = "event";
    public const string Competition = "competition";
    public const string Upcoming = "upcoming";
}

public static class CalendarMeetTypes
{
    public const string Pool = "pool";
    public const string OpenWater = "open_water";
    public const string Artistic = "artistic";
}

public static class CalendarMeetStatuses
{
    public const string Upcoming = "upcoming";
    public const string Live = "live";
    public const string InProgress = "in_progress";
    public const string Complete = "complete";
    public const string Done = "done";
    public const string Cancelled = "cancelled";
    public const string Postponed = "postponed";
}
