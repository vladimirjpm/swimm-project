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

    /* Денормализация для клиентских фильтров страницы My media (дизайн-бриф §6.1):
       фильтрация по сезону/соревнованию/клубу идёт на клиенте, сервер отдаёт поля. */

    [JsonPropertyName("competition_name")]
    public string? CompetitionName { get; set; }

    /// <summary>Дата соревнования в формате данных (dd/MM/yyyy).</summary>
    [JsonPropertyName("competition_date")]
    public string? CompetitionDate { get; set; }

    /// <summary>Клуб пловца в этом заплыве (только при привязке к заплыву).</summary>
    [JsonPropertyName("club_name")]
    public string? ClubName { get; set; }

    /* Реакции (❤ на медиа) — Sys_UserReactions, страница My media v3. */

    [JsonPropertyName("likes_count")]
    public int LikesCount { get; set; }

    /// <summary>Текущий пользователь уже лайкнул это медиа.</summary>
    [JsonPropertyName("my_like")]
    public bool MyLike { get; set; }
}

/// <summary>Соревнование пловца для пикера привязки (Add link, шаг 3).</summary>
public class SwimmerCompetitionBriefDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;
}

/// <summary>Заплыв пловца для пикера привязки (Add link, шаг 3).</summary>
public class SwimmerResultBriefDto
{
    [JsonPropertyName("result_id")]
    public long ResultId { get; set; }

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = string.Empty;

    [JsonPropertyName("style")]
    public string Style { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;
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
