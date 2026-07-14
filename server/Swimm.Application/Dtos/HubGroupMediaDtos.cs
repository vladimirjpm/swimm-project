using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>Одна медиа-запись группы (публичная галерея или медиа тренировки).</summary>
public sealed class HubGroupMediaDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>image / video / album</summary>
    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";

    /// <summary>youtube / vimeo / album / other</summary>
    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }
}

/// <summary>
/// Медиа members-слоя (тренерские разборы, 2B′): запись + контекст якоря для отображения.
/// Отдаётся только активным user-членам группы / админам (см. HubGroupsController).
/// </summary>
public sealed class HubGroupMemberMediaDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Якорь-пловец (null — общее members-медиа группы).</summary>
    [JsonPropertyName("swimmer_id")]
    public int? SwimmerId { get; set; }

    [JsonPropertyName("swimmer_name")]
    public string? SwimmerName { get; set; }

    [JsonPropertyName("swimmer_name_en")]
    public string? SwimmerNameEn { get; set; }

    /// <summary>Якорь-заплыв (null — медиа без привязки к заплыву).</summary>
    [JsonPropertyName("result_id")]
    public long? ResultId { get; set; }

    /// <summary>Контекст заплыва для карточки: «freestyle 100 · 01/07/2026 · Competition».</summary>
    [JsonPropertyName("result_label")]
    public string? ResultLabel { get; set; }
}

/// <summary>Вход для POST /api/hub-groups/{id}/media.</summary>
public sealed class HubGroupMediaInputDto
{
    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    /// <summary>null = публичная галерея; иначе — id тренировки этой же группы.</summary>
    [JsonPropertyName("training_id")]
    public int? TrainingId { get; set; }

    /// <summary>public (дефолт, витрина) | members (разборы; только официальная группа).
    /// Игнорируется для медиа тренировок (training_id задан).</summary>
    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }

    /// <summary>Якорь-пловец (только при visibility=members).</summary>
    [JsonPropertyName("swimmer_id")]
    public int? SwimmerId { get; set; }

    /// <summary>Якорь-заплыв (только при visibility=members); swimmer_id выводится из заплыва.</summary>
    [JsonPropertyName("result_id")]
    public long? ResultId { get; set; }
}
