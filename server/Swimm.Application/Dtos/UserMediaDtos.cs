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

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Level не принимаем — 2A поддерживает только "swimmer", сервер ставит его сам.
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
}
