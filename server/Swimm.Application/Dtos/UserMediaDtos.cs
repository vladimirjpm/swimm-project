using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

public class UserMediaDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("result_id")]
    public long? ResultId { get; set; }

    [JsonPropertyName("competition_id")]
    public int? CompetitionId { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Имя пловца (для сводной страницы «My media» — медиа разных пловцов).</summary>
    [JsonPropertyName("swimmer_name")]
    public string? SwimmerName { get; set; }

    /// <summary>Подпись заплыва (стиль/дистанция/дата), если привязано к заплыву.</summary>
    [JsonPropertyName("result_label")]
    public string? ResultLabel { get; set; }
}

/// <summary>
/// Level не принимаем — сервер выводит его сам из result_id/competition_id (см.
/// UserMediaRepository.AddAsync): есть result_id → "result"; нет result_id, есть
/// competition_id → "competition"; ничего → "swimmer".
/// Visibility не принимаем — всегда "private" (2A: личное, не публичное).
/// </summary>
public class AddUserMediaRequest
{
    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>Опционально — привязка к конкретному заплыву; владение проверяется в репозитории.</summary>
    [JsonPropertyName("result_id")]
    public long? ResultId { get; set; }

    /// <summary>Опционально — привязка к соревнованию; игнорируется, если задан ResultId.</summary>
    [JsonPropertyName("competition_id")]
    public int? CompetitionId { get; set; }
}
