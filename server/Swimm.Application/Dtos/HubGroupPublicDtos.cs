using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>Карточка группы в публичном списке /api/hub-groups.</summary>
public sealed class HubGroupListItemDto
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string? NameEn { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("club_name")]
    public string? ClubName { get; set; }

    [JsonPropertyName("member_count")]
    public int MemberCount { get; set; }
}

/// <summary>Публичная ссылка группы (whatsapp/telegram/instagram/site).</summary>
public sealed class HubGroupPublicLinkDto
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

/// <summary>Участник группы на публичной странице.</summary>
public sealed class HubGroupPublicMemberDto
{
    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string NameEn { get; set; } = "";

    [JsonPropertyName("birth_year")]
    public int BirthYear { get; set; }

    [JsonPropertyName("club_name")]
    public string? ClubName { get; set; }

    /// <summary>member | captain | coach</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = "member";
}

/// <summary>«Рекорд группы» — лучшее время участников по оси стиль+дистанция+бассейн.</summary>
public sealed class HubGroupBestDto
{
    [JsonPropertyName("style_name")]
    public string StyleName { get; set; } = "";

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = "";

    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "";

    [JsonPropertyName("time_original")]
    public string TimeOriginal { get; set; } = "";

    [JsonPropertyName("time_millisecond")]
    public int? TimeMillisecond { get; set; }

    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("swimmer_name")]
    public string SwimmerName { get; set; } = "";

    [JsonPropertyName("swimmer_name_en")]
    public string SwimmerNameEn { get; set; } = "";

    [JsonPropertyName("competition_name")]
    public string CompetitionName { get; set; } = "";

    /// <summary>dd/MM/yyyy — формат дат соревнований во всех публичных ответах.</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("points")]
    public int Points { get; set; }
}

/// <summary>
/// Полная публичная страница группы (/api/hub-groups/{slug}).
/// Тот же контракт отдаёт /api/hub-groups/favorites — виртуальная группа
/// «Моё избранное» поверх Sys_UserFavorites (is_virtual=true, slug="favorites").
/// </summary>
public sealed class HubGroupDetailsDto
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string? NameEn { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("cover_image_url")]
    public string? CoverImageUrl { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("club_name")]
    public string? ClubName { get; set; }

    [JsonPropertyName("links")]
    public List<HubGroupPublicLinkDto> Links { get; set; } = [];

    /// <summary>true у виртуальной группы «Моё избранное».</summary>
    [JsonPropertyName("is_virtual")]
    public bool IsVirtual { get; set; }

    [JsonPropertyName("members")]
    public List<HubGroupPublicMemberDto> Members { get; set; } = [];

    /// <summary>Последние заплывы участников (свежие сверху).</summary>
    [JsonPropertyName("recent_results")]
    public List<ResultDto> RecentResults { get; set; } = [];

    /// <summary>Рекорды группы: лучшее время по каждой оси стиль+дистанция+бассейн.</summary>
    [JsonPropertyName("bests")]
    public List<HubGroupBestDto> Bests { get; set; } = [];
}
