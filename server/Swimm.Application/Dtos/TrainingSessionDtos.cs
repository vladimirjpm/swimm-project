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

    /// <summary>
    /// Сколько медиа ДРУГИХ добавлено после того, как зритель открывал ленту (<c>PUT …/seen</c>),
    /// но не старше 14 дней — акцентная метка «новые видео» (Т1б, `marks-spec.md`).
    /// </summary>
    [JsonPropertyName("new_media_count")]
    public int NewMediaCount { get; set; }
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

    /// <summary>Показатели карточки (Р2): объём и число пловцов; null — времён нет.</summary>
    [JsonPropertyName("stats")]
    public TrainingSessionStatsDto? Stats { get; set; }

    /// <summary>Ключевой сет — ★ тренера (Р4); null — не помечен.</summary>
    [JsonPropertyName("key_set_no")]
    public int? KeySetNo { get; set; }

    /// <summary>Строка «You» (Р4): мой быстрейший повтор в ключевом сете; null — строки нет.</summary>
    [JsonPropertyName("my_key")]
    public TrainingSessionMyKeyDto? MyKey { get; set; }
}

/// <summary>Показатели тренировки в ленте (docs/plans/trainings-tab-redesign-plan.md, Р2).</summary>
public sealed class TrainingSessionStatsDto
{
    /// <summary>Объём одного пловца, метры: сумма уникальных повторов с временем (клиент пишет «2.4 km»).</summary>
    [JsonPropertyName("meters")]
    public int Meters { get; set; }

    /// <summary>Сколько пловцов записано временами.</summary>
    [JsonPropertyName("swimmers")]
    public int Swimmers { get; set; }
}

/// <summary>
/// «You» в карточке тренировки (Р4): быстрейший повтор «моего» пловца (сам, затем семья) в сете,
/// помеченном тренером. Время — тренировочный замер (И11 к нему не применяется).
/// </summary>
public sealed class TrainingSessionMyKeyDto
{
    [JsonPropertyName("set_no")]
    public int SetNo { get; set; }

    [JsonPropertyName("order_no")]
    public int OrderNo { get; set; }

    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    /// <summary>me / family — чей повтор (как <c>LanePlan.my_swimmers</c>).</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string NameEn { get; set; } = "";

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = "";

    [JsonPropertyName("style")]
    public string Style { get; set; } = "";

    /// <summary>Время как записано в источнике.</summary>
    [JsonPropertyName("time")]
    public string Time { get; set; } = "";
}

/// <summary>
/// «Sets & times» тренировки (Т1а): <c>GET /api/hub-groups/{id}/sessions/{sid}/sets</c>, лениво
/// при раскрытии. ЛИЧНЫЙ ответ — та же аудитория, что у списка, без кэша.
/// </summary>
public sealed class TrainingSessionSetsDto
{
    [JsonPropertyName("session_id")]
    public int SessionId { get; set; }

    [JsonPropertyName("key_set_no")]
    public int? KeySetNo { get; set; }

    /// <summary>Зритель управляет группой — может ставить ★ ключевому сету.</summary>
    [JsonPropertyName("can_manage")]
    public bool CanManage { get; set; }

    [JsonPropertyName("sets")]
    public List<TrainingSetDto> Sets { get; set; } = [];
}

public sealed class TrainingSetDto
{
    [JsonPropertyName("set_no")]
    public int SetNo { get; set; }

    /// <summary>Этот сет помечен тренером ★.</summary>
    [JsonPropertyName("is_key")]
    public bool IsKey { get; set; }

    [JsonPropertyName("rows")]
    public List<TrainingSetRowDto> Rows { get; set; } = [];
}

/// <summary>Строка «1.1 · имя · время · V3 · уровень» (5a #4). Время — тренировочный замер.</summary>
public sealed class TrainingSetRowDto
{
    [JsonPropertyName("order_no")]
    public int OrderNo { get; set; }

    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string NameEn { get; set; } = "";

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = "";

    [JsonPropertyName("style")]
    public string Style { get; set; } = "";

    /// <summary>Время как записано; null — повтор без замера.</summary>
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    /// <summary>v1..v5; null — не задана.</summary>
    [JsonPropertyName("intensity")]
    public string? Intensity { get; set; }

    /// <summary>Текущий уровень пловца в группе; null — без уровня.</summary>
    [JsonPropertyName("level")]
    public LanePlanLevelDto? Level { get; set; }

    /// <summary>Пловец из круга «моих» (сам или семья) — строка подсвечена, фильтр «Me».</summary>
    [JsonPropertyName("mine")]
    public bool Mine { get; set; }
}

/// <summary>★ ключевого сета (Р4): номер сета этой тренировки; null — снять пометку.</summary>
public sealed class TrainingKeySetInputDto
{
    [JsonPropertyName("set_no")]
    public int? SetNo { get; set; }
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

    /// <summary>Новое для зрителя (см. <c>new_media_count</c>) — подпись «NEW» на плитке.</summary>
    [JsonPropertyName("is_new")]
    public bool IsNew { get; set; }
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
