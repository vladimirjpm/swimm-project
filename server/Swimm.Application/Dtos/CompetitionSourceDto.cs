using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Элемент списка источников для клиентского DDL: одно многодневное событие ИЛИ
/// одно однодневное соревнование. Многодневное событие сворачивается в ОДНУ запись.
/// </summary>
public sealed class CompetitionSourceDto
{
    /// <summary>"event" — многодневное событие; "competition" — однодневное соревнование.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "competition";

    /// <summary>EventId (для kind=event) либо CompetitionId (для kind=competition).</summary>
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Дата (для события — первого дня), dd/MM/yyyy.</summary>
    [JsonPropertyName("date")]
    public string Date { get; init; } = string.Empty;

    /// <summary>Дата последнего дня события (null для однодневных и одно­дневных событий).</summary>
    [JsonPropertyName("date_end")]
    public string? DateEnd { get; init; }

    [JsonPropertyName("pool_type")]
    public string PoolType { get; init; } = string.Empty;

    [JsonPropertyName("is_masters")]
    public bool IsMasters { get; init; }

    [JsonPropertyName("is_award")]
    public bool IsAward { get; init; }

    [JsonPropertyName("show_combine_all_results")]
    public bool ShowCombineAllResults { get; init; }

    /// <summary>Категория для селектора: "young8_11" | "junior" | "masters".
    /// Выводится: is_masters → masters; макс. возраст результатов ≤ 11 → young8_11; иначе junior.</summary>
    [JsonPropertyName("category")]
    public string Category { get; init; } = "junior";

    /// <summary>Статус по датам: "live" (идёт сегодня) | "upcoming" (в будущем) | "done".</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "done";

    [JsonPropertyName("day_count")]
    public int DayCount { get; init; }

    /// <summary>Даты дней (dd/MM/yyyy, по возрастанию): для события — все дни, для однодневного —
    /// одна. В paged-режиме клиент берёт опции фильтра по дню отсюда (страница их не покрывает).</summary>
    [JsonPropertyName("day_dates")]
    public IReadOnlyList<string> DayDates { get; init; } = [];

    [JsonPropertyName("result_count")]
    public int ResultCount { get; init; }
}
