using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Публикация личного медиа в КОЛЛЕКТИВЕ — как её видит владелец медиа.
///
/// Цель полиморфна: группа или клуб (`target_type`). Раньше полей было два — `hub_group_id`
/// и `hub_group_name`; они переименованы в `target_*`, а не продублированы, чтобы у клиента
/// не оказалось двух источников правды об одном и том же.
/// </summary>
public class UserMediaPublicationDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("user_media_id")]
    public int UserMediaId { get; set; }

    /// <summary>group | club.</summary>
    [JsonPropertyName("target_type")]
    public string TargetType { get; set; } = string.Empty;

    /// <summary>Id группы либо клуба — смотри <see cref="TargetType"/>.</summary>
    [JsonPropertyName("target_id")]
    public int TargetId { get; set; }

    [JsonPropertyName("target_name")]
    public string TargetName { get; set; } = string.Empty;

    /// <summary>members | public. У клубной цели бывает только public — членов у клуба нет.</summary>
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

/// <summary>Заявка владельца: опубликовать медиа в коллективе на уровне members|public.</summary>
public class SubmitPublicationRequest
{
    /// <summary>group | club.</summary>
    [JsonPropertyName("target_type")]
    public string TargetType { get; set; } = string.Empty;

    [JsonPropertyName("target_id")]
    public int TargetId { get; set; }

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
    /// <summary>null — медиа уровня «соревнование» (не привязано к заплыву).</summary>
    [JsonPropertyName("result_id")]
    public long? ResultId { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("source_type")]
    public string SourceType { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Коллектив, куда владелец может подать конкретное медиа (селектор подачи): группа, где
/// пловец в ростере и податель свой, либо КЛУБ пловца — там ростер бесплатный, он приходит
/// из справочника федерации.
/// </summary>
public class PublishTargetDto
{
    /// <summary>group | club.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
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

    /* Цель — для сводного inbox-а по всему, что я модерирую (My media → Moderation). */

    /// <summary>group | club.</summary>
    [JsonPropertyName("target_type")]
    public string TargetType { get; set; } = string.Empty;

    [JsonPropertyName("target_id")]
    public int TargetId { get; set; }

    [JsonPropertyName("target_name")]
    public string TargetName { get; set; } = string.Empty;

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

    /// <summary>
    /// Соревнование медиа: день заплыва-якоря, а если медиа подано на всё соревнование —
    /// оно само. Нужен, чтобы подпись вела в протокол (`routes.competitionSwims` просит id).
    /// </summary>
    [JsonPropertyName("competition_id")]
    public int? CompetitionId { get; set; }
}
