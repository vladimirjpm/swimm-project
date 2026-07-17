using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>Публикация личного медиа в группе — как её видит владелец медиа.</summary>
public class UserMediaPublicationDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("user_media_id")]
    public int UserMediaId { get; set; }

    [JsonPropertyName("hub_group_id")]
    public int HubGroupId { get; set; }

    [JsonPropertyName("hub_group_name")]
    public string HubGroupName { get; set; } = string.Empty;

    /// <summary>members | public.</summary>
    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;

    /// <summary>pending | approved | rejected.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("decided_at")]
    public DateTime? DecidedAt { get; set; }
}

/// <summary>Заявка владельца: опубликовать медиа в группе на уровне members|public.</summary>
public class SubmitPublicationRequest
{
    [JsonPropertyName("hub_group_id")]
    public int HubGroupId { get; set; }

    /// <summary>members | public.</summary>
    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;
}

/// <summary>
/// Медиа, привязанное к заплыву и видимое текущему зрителю (своё + одобренные публикации) —
/// для иконок видео в общей таблице результатов.
/// </summary>
public class VisibleResultMediaDto
{
    [JsonPropertyName("result_id")]
    public long ResultId { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

/// <summary>Решение админа группы по заявке.</summary>
public class PublicationDecisionRequest
{
    /// <summary>true → approved; false → rejected (для approved это «снять с публикации»).</summary>
    [JsonPropertyName("approve")]
    public bool Approve { get; set; }
}

/// <summary>Строка inbox-а модерации для админа группы.</summary>
public class GroupPublicationInboxItemDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    /* — контекст медиа (владельцу заявки виден он сам, админу — что именно публикуется) — */

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("owner_user_id")]
    public int OwnerUserId { get; set; }

    [JsonPropertyName("owner_email")]
    public string? OwnerEmail { get; set; }

    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("swimmer_name")]
    public string? SwimmerName { get; set; }

    [JsonPropertyName("result_id")]
    public long? ResultId { get; set; }

    /// <summary>Подпись заплыва (стиль/дистанция/дата), если медиа привязано к заплыву.</summary>
    [JsonPropertyName("result_label")]
    public string? ResultLabel { get; set; }
}
