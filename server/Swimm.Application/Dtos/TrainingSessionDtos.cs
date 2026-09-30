using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Тренировки группы списком — и с временами, и без (docs/plans/group-events-media-plan.md, Е1):
/// <c>GET /api/hub-groups/{id}/sessions</c>. ЛИЧНЫЙ ответ, как журнал тренировок: управляющим и
/// активным участникам, без кэша. Времён тут нет — только сама тренировка и её медиа.
/// </summary>
public sealed class TrainingSessionListDto
{
    [JsonPropertyName("sessions")]
    public List<TrainingSessionDto> Sessions { get; set; } = [];

    /// <summary>Зритель управляет группой: ему «+ New training», правка и удаление.</summary>
    [JsonPropertyName("can_manage")]
    public bool CanManage { get; set; }

    /// <summary>
    /// Зритель может добавить видео к тренировке (Е2): управляющий — всегда, участник — если
    /// группа не поставила «только тренер» (<c>training_media</c> в расписании).
    /// </summary>
    [JsonPropertyName("can_add_media")]
    public bool CanAddMedia { get; set; }
}

public sealed class TrainingSessionDto
{
    /// <summary>Id сессии (<c>Sys_TrainingSessions.Id</c>) — к нему цепляется медиа.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>yyyy-MM-dd.</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    /// <summary>«HH:mm» — связка с занятием расписания; null — не задано.</summary>
    [JsonPropertyName("start")]
    public string? Start { get; set; }

    /// <summary>Название от тренера («Underwater filming»); у залитых скриптом — null.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>План тренировки из источника («200->2x100»); у заведённых кнопкой — null.</summary>
    [JsonPropertyName("plan")]
    public string? Plan { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }

    /// <summary>Есть записанные времена: дату и бассейн не поменять, удалить нельзя.</summary>
    [JsonPropertyName("has_results")]
    public bool HasResults { get; set; }

    /// <summary>Заведена кнопкой (а не залита скриптом).</summary>
    [JsonPropertyName("created_in_app")]
    public bool CreatedInApp { get; set; }

    [JsonPropertyName("media")]
    public List<TrainingSessionMediaDto> Media { get; set; } = [];
}

/// <summary>
/// Медиа тренировки — ссылка (сайт ничего не хостит). Кто добавил, наружу не отдаём (как в лентах
/// группы, important.md): только «моё» и «могу удалить» — для зрителя этого запроса.
/// </summary>
public sealed class TrainingSessionMediaDto
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

    /// <summary>Добавил сам зритель.</summary>
    [JsonPropertyName("mine")]
    public bool Mine { get; set; }

    /// <summary>Зритель может удалить: своё — автор, любое — управляющий.</summary>
    [JsonPropertyName("can_delete")]
    public bool CanDelete { get; set; }
}

/// <summary>Добавить ссылку к тренировке (Е2). Проверки — те же, что у медиа группы.</summary>
public sealed class TrainingSessionMediaInputDto
{
    /// <summary>image / video / album</summary>
    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    /// <summary>youtube / vimeo / album / other</summary>
    [JsonPropertyName("source_type")]
    public string? SourceType { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }
}

/// <summary>Итог добавления ссылки: либо медиа, либо код отказа (400 / 403 / 404).</summary>
public sealed class TrainingSessionMediaSaveResult
{
    public TrainingSessionMediaDto? Media { get; init; }
    public string? Error { get; init; }
    public int Status { get; init; }

    public static TrainingSessionMediaSaveResult Ok(TrainingSessionMediaDto dto) => new() { Media = dto };
    public static TrainingSessionMediaSaveResult Fail(int status, string error) => new() { Status = status, Error = error };
}

/// <summary>Завести / поправить тренировку. Всё, кроме даты и названия, — по желанию.</summary>
public sealed class TrainingSessionInputDto
{
    /// <summary>yyyy-MM-dd.</summary>
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    /// <summary>«HH:mm»; пусто при создании — берётся из расписания, если в этот день ровно одно занятие.</summary>
    [JsonPropertyName("start")]
    public string? Start { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>25m / 50m; пусто при создании — из расписания группы, при правке — не меняется.</summary>
    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }
}

/// <summary>Итог записи: либо тренировка, либо код отказа (400 / 404).</summary>
public sealed class TrainingSessionSaveResult
{
    public TrainingSessionDto? Session { get; init; }
    public string? Error { get; init; }
    public int Status { get; init; }

    public static TrainingSessionSaveResult Ok(TrainingSessionDto dto) => new() { Session = dto };
    public static TrainingSessionSaveResult Fail(int status, string error) => new() { Status = status, Error = error };
}
