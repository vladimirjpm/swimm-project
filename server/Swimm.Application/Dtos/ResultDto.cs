using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// DTO результата для JSON API.
/// Содержит все денормализованные данные без дополнительных JOIN.
/// </summary>
public class ResultDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("competition")]
    public string CompetitionName { get; set; } = string.Empty;

    [JsonPropertyName("is_masters")]
    public bool IsMasters { get; set; }

    [JsonPropertyName("is_award")]
    public bool IsAward { get; set; }

    [JsonPropertyName("age_group")]
    public string AgeGroup { get; set; } = string.Empty;

    /// <summary>Дата в формате dd/MM/yyyy (берётся из Competition.Date как есть).</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    /// <summary>
    /// Отображаемое название события (напр. "200 חופשי - בנות 12").
    /// ОТЛОЖЕНО: в БД не хранится (импорт его теряет) — пока отдаётся пустым.
    /// </summary>
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("event_style_name")]
    public string StyleName { get; set; } = string.Empty;

    [JsonPropertyName("event_style_len")]
    public string Distance { get; set; } = string.Empty;

    [JsonPropertyName("event_style_gender")]
    public string Gender { get; set; } = string.Empty;

    [JsonPropertyName("event_style_age")]
    public string EventStyleAge { get; set; } = string.Empty;

    [JsonPropertyName("pool_type")]
    public string PoolType { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public int? Position { get; set; }

    [JsonPropertyName("position_age_group")]
    public int? PositionAgeGroup { get; set; }

    [JsonPropertyName("heat")]
    public int Heat { get; set; }

    [JsonPropertyName("lane")]
    public int Lane { get; set; }

    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name_en")]
    public string LastNameEn { get; set; } = string.Empty;

    [JsonPropertyName("first_name_en")]
    public string FirstNameEn { get; set; } = string.Empty;

    [JsonPropertyName("birth_year")]
    public int BirthYear { get; set; }

    [JsonPropertyName("club")]
    public string ClubName { get; set; } = string.Empty;

    [JsonPropertyName("club_en")]
    public string ClubNameEn { get; set; } = string.Empty;

    [JsonPropertyName("time_ms")]
    public int? TimeMillisecond { get; set; }

    [JsonPropertyName("time")]
    public string TimeOriginal { get; set; } = string.Empty;

    [JsonPropertyName("time_split")]
    public string TimeSplit { get; set; } = string.Empty;

    [JsonPropertyName("time_fail")]
    public bool TimeFail { get; set; }

    [JsonPropertyName("time_fail_note")]
    public string? TimeFailNote { get; set; }

    [JsonPropertyName("international_points")]
    public int InternationalPoints { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("is_relay")]
    public bool IsRelay { get; set; }

    [JsonPropertyName("relay_team_name")]
    public string? RelayTeamName { get; set; }

    [JsonPropertyName("relay_swimmers_name")]
    public string? RelaySwimmersName { get; set; }

    // relay_swimmers[] (структурный состав) ОТЛОЖЕН: в БД хранится только Relay.SwimmersName
    // (строка), структурированного массива нет — восстановить нельзя.

    [JsonPropertyName("gallery")]
    public List<GalleryItemDto>? Gallery { get; set; }
}

/// <summary>Элемент галереи для JSON API (совпадает с клиентским GalleryItem).</summary>
public class GalleryItemDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("sourceType")]
    public string? SourceType { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }
}
