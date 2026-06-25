using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

public class FavoriteDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("target_type")]
    public string TargetType { get; set; } = string.Empty;

    [JsonPropertyName("swimmer_id")]
    public int? SwimmerId { get; set; }

    [JsonPropertyName("swimmer_name")]
    public string? SwimmerName { get; set; }

    [JsonPropertyName("club_id")]
    public int? ClubId { get; set; }

    [JsonPropertyName("club_name")]
    public string? ClubName { get; set; }

    [JsonPropertyName("is_primary")]
    public bool IsPrimary { get; set; }

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

public class AddFavoriteRequest
{
    [JsonPropertyName("target_type")]
    public string TargetType { get; set; } = string.Empty;

    [JsonPropertyName("swimmer_id")]
    public int? SwimmerId { get; set; }

    [JsonPropertyName("club_id")]
    public int? ClubId { get; set; }
}

public class ReorderItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }
}
