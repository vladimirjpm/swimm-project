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
}
