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

    /// <summary>
    /// Клуб избранного ПЛОВЦА (у записи-клуба — null): эмблема и название на странице My
    /// favorites. Название — исходное (<c>Club.Name</c>, иврит): по нему ищется файл эмблемы;
    /// нет ивритского — английское.
    /// </summary>
    [JsonPropertyName("swimmer_club_id")]
    public int? SwimmerClubId { get; set; }

    [JsonPropertyName("swimmer_club_name")]
    public string? SwimmerClubName { get; set; }

    [JsonPropertyName("club_id")]
    public int? ClubId { get; set; }

    [JsonPropertyName("club_name")]
    public string? ClubName { get; set; }

    [JsonPropertyName("is_primary")]
    public bool IsPrimary { get; set; }

    /// <summary>Пометка «семья» (золотое сердечко) — только у пловцов, прав не даёт.</summary>
    [JsonPropertyName("is_family")]
    public bool IsFamily { get; set; }

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

public enum AddFavoriteStatus
{
    Added,
    /// <summary>Уже в избранном (409, как было до лимита).</summary>
    Duplicate,
    /// <summary>Лимит типа выбран (422 с кодом <c>FavoritesRules.LimitErrorCode</c>).</summary>
    LimitReached,
}

/// <summary>
/// Исход добавления в избранное. Раньше «не добавилось» было одним null и значило только
/// «дубль»; с лимитом у отказа две причины, и клиенту нужно их различать: на дубль молчат, на
/// лимит гасят сердечко с подсказкой.
/// </summary>
public sealed record AddFavoriteResult(
    AddFavoriteStatus Status,
    FavoriteDto? Favorite = null,
    int? Limit = null,
    string? Message = null)
{
    public static AddFavoriteResult Added(FavoriteDto favorite) => new(AddFavoriteStatus.Added, favorite);
    public static AddFavoriteResult Duplicate() => new(AddFavoriteStatus.Duplicate);
    public static AddFavoriteResult LimitReached(int limit, string message) =>
        new(AddFavoriteStatus.LimitReached, Limit: limit, Message: message);
}

/// <summary>Исход пометки «семья»: сделано, нет такой записи (чужая/клуб), семья заполнена.</summary>
public enum SetFamilyStatus
{
    Done,
    NotFound,
    /// <summary>Уже <c>FavoritesRules.MaxFamily</c> в семье — 422 с кодом <c>FamilyLimitErrorCode</c>.</summary>
    LimitReached,
}

public class ReorderItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }
}
