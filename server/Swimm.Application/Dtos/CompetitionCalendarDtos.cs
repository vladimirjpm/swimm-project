using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Календарь сезона для главной <c>/competitions</c> (docs/plans/competitions-home-plan.md,
/// часть Б, шаг Б1). Источник — только наша база (решение В2): справочник <c>Competitions</c>
/// (дни события свёрнуты в одну запись) + предстоящие старты, у которых уже есть стартовый
/// протокол (<c>CompetitionEntries</c>), но ещё нет строки в справочнике.
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
    /// <summary>Уникальный ключ строки: <c>event:12</c> | <c>competition:1766</c> | <c>upcoming:16900</c>.</summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    /// <summary>"event" — многодневное событие (ссылка <c>/results?eventId=</c>); "competition" —
    /// однодневное (<c>/competitions/{id}</c>); "upcoming" — только стартовый протокол
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
    /// Последние два — в контракте, но сервер их пока не отдаёт (решение В4, источника нет).</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = CalendarMeetStatuses.Done;

    /// <summary>Новая дата перенесённого старта (статус postponed). Пока всегда null (В4).</summary>
    [JsonPropertyName("postponed_to")]
    public string? PostponedTo { get; init; }

    /// <summary>Есть стартовый протокол (заявки в <c>CompetitionEntries</c>).</summary>
    [JsonPropertyName("has_start_list")]
    public bool HasStartList { get; init; }
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
