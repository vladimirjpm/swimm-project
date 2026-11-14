using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

// План дорожек группы (docs/plans/lane-plans-plan.md, L2). Его читают и участники, поэтому
// ручки живут под /api/hub-groups/{id}/lane-plans и говорят snake_case, как публичные ответы
// групп и тренировки. Вход — тоже snake_case: camelCase биндер молча потерял бы поле.

/// <summary>Строка списка планов (выбор даты).</summary>
public sealed class LanePlanSummaryDto
{
    /// <summary>yyyy-MM-dd.</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("lane_count")]
    public int LaneCount { get; set; }

    /// <summary>Лимит людей на дорожку (перелив по подуровням); null — без лимита.</summary>
    [JsonPropertyName("max_per_lane")]
    public int? MaxPerLane { get; set; }

    /// <summary>
    /// Сколько дорожек «Auto-arrange» оставляет пустыми про запас (0..11). Запас держится всегда,
    /// и когда дорожек не хватает; людям остаётся хотя бы одна.
    /// </summary>
    [JsonPropertyName("reserved_lanes")]
    public int ReservedLanes { get; set; }
}

/// <summary>Уровень на дорожке — подпись и цвет (null-цвет клиент красит по рангу).</summary>
public sealed class LanePlanLevelDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("color")]
    public string? Color { get; set; }
}

/// <summary>Пловец на доске: имя на иврите (правило имён), EN — фоллбек.</summary>
public sealed class LanePlanSwimmerDto
{
    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string NameEn { get; set; } = "";

    [JsonPropertyName("birth_year")]
    public int BirthYear { get; set; }

    /// <summary>ТЕКУЩИЙ уровень пловца в группе (не снимок) — точка цвета в редакторе.</summary>
    [JsonPropertyName("level_id")]
    public int? LevelId { get; set; }

    /// <summary>ТЕКУЩИЙ подуровень (1 — верхний); null — не задан или у уровня подуровней нет.</summary>
    [JsonPropertyName("sub_level")]
    public int? SubLevel { get; set; }

    /// <summary>Пловца уже нет в составе группы — в плане он остался (план — снимок).</summary>
    [JsonPropertyName("left_group")]
    public bool LeftGroup { get; set; }

    /// <summary>
    /// На перерыве в день плана (Ш3.1, «On break»): редактор не кладёт его в Unassigned новой
    /// раскладки; руками перетащить можно.
    /// </summary>
    [JsonPropertyName("on_break")]
    public bool OnBreak { get; set; }

    /// <summary>
    /// Закреплён на дорожке (тренер перенёс руками, CF2): «Auto-arrange» его не двигает.
    /// Только управляющему — участнику всегда false.
    /// </summary>
    [JsonPropertyName("pinned")]
    public bool Pinned { get; set; }
}

public sealed class LanePlanLaneDto
{
    [JsonPropertyName("lane_no")]
    public int LaneNo { get; set; }

    [JsonPropertyName("level")]
    public LanePlanLevelDto? Level { get; set; }

    [JsonPropertyName("workout")]
    public string? Workout { get; set; }

    /// <summary>В порядке дорожки (первый ведёт).</summary>
    [JsonPropertyName("swimmers")]
    public List<LanePlanSwimmerDto> Swimmers { get; set; } = [];
}

/// <summary>Доска плана на дату.</summary>
public sealed class LanePlanDto
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("lane_count")]
    public int LaneCount { get; set; }

    /// <summary>Лимит людей на дорожку (перелив по подуровням); null — без лимита.</summary>
    [JsonPropertyName("max_per_lane")]
    public int? MaxPerLane { get; set; }

    /// <summary>
    /// Сколько дорожек «Auto-arrange» оставляет пустыми про запас (0..11). Запас держится всегда,
    /// и когда дорожек не хватает; людям остаётся хотя бы одна.
    /// </summary>
    [JsonPropertyName("reserved_lanes")]
    public int ReservedLanes { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("lanes")]
    public List<LanePlanLaneDto> Lanes { get; set; } = [];

    /// <summary>Пришли, но не разложены по дорожкам.</summary>
    [JsonPropertyName("unassigned")]
    public List<LanePlanSwimmerDto> Unassigned { get; set; } = [];

    /// <summary>
    /// Состав группы, которого в плане нет («сегодня не плывут»). Только управляющему — участнику
    /// незачем видеть, кого тренер снял; ему приходит пустой список.
    /// </summary>
    [JsonPropertyName("not_today")]
    public List<LanePlanSwimmerDto> NotToday { get; set; } = [];

    /// <summary>Зритель может править план (кнопки редактора).</summary>
    [JsonPropertyName("can_edit")]
    public bool CanEdit { get; set; }

    /// <summary>
    /// «Мои» пловцы ЭТОГО плана — для карточки «Your lane» (L4). Только подсветка, прав не даёт:
    /// источники — привязка аккаунта админом сайта, избранное «Me» и семья, метка членства в
    /// группе («за какого пловца этот аккаунт»). Сперва «me», потом семья.
    /// </summary>
    [JsonPropertyName("my_swimmers")]
    public List<LanePlanMySwimmerDto> MySwimmers { get; set; } = [];
}

/// <summary>
/// Кто из видимого состава в день плана «не в воде» — On break или Doesn't train (Р74; «Going» на
/// эту дату их возвращает), либо ответил «Can't come» на все свои занятия этой даты. Редактор
/// кладёт их в «Not today» нового плана и копии, а не в Unassigned: иначе «Auto lanes» и
/// «Distribute» ставят их на дорожки. Сохранённый план не трогается — там решил тренер.
/// </summary>
public sealed class LanePlanAwayDto
{
    [JsonPropertyName("swimmer_ids")]
    public List<int> SwimmerIds { get; set; } = [];
}

/// <summary>
/// Что мешает опубликовать план (docs/plans/trainings-coach-flow-plan.md, решение 10): идущие
/// («Going» на занятие этой даты) пловцы без уровня, которые не стоят на дорожке. Пусто — можно.
/// </summary>
public sealed class LanePlanPublishCheckDto
{
    [JsonPropertyName("blockers")]
    public List<LanePlanBlockerDto> Blockers { get; set; } = [];
}

public sealed class LanePlanBlockerDto
{
    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string NameEn { get; set; } = "";
}

/// <summary>Тело «Publish»: <c>notify</c> — разослать участникам push «дорожки готовы» (CF3).</summary>
public sealed class LanePlanPublishInputDto
{
    [JsonPropertyName("notify")]
    public bool Notify { get; set; }
}

/// <summary>
/// Итог «Publish»: плана нет / отказ с текстом (решение 10) / опубликован. <see cref="Changed"/> —
/// статус действительно сменился (повторный Publish уведомление не шлёт).
/// </summary>
public sealed record LanePlanPublishResult(bool Found, string? Error, bool Changed)
{
    public bool Success => Found && Error == null;
}

/// <summary>«Мой» пловец плана: <c>me</c> — сам зритель, <c>family</c> — за кого он смотрит.</summary>
public sealed class LanePlanMySwimmerDto
{
    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    /// <summary>me | family.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "me";

    /// <summary>
    /// С какой дорожки тренер перенёс пловца в опубликованном плане после того, как зритель
    /// последний раз открывал вид Lanes (<c>PUT …/lane-plans/seen</c>) — золотая метка (Т1б).
    /// null — не переносили или перенос уже видел.
    /// </summary>
    [JsonPropertyName("moved_from_lane")]
    public int? MovedFromLane { get; set; }
}

/// <summary>Значения по умолчанию входа плана — когда клиент поле не прислал.</summary>
public static class LanePlanDefaults
{
    /// <summary>Запасных дорожек у нового плана: одна.</summary>
    public const int ReservedLanes = 1;
}

// ── Вход ────────────────────────────────────────────────────────────────────

public sealed class LanePlanLaneInputDto
{
    [JsonPropertyName("lane_no")]
    public int LaneNo { get; set; }

    [JsonPropertyName("level_id")]
    public int? LevelId { get; set; }

    [JsonPropertyName("workout")]
    public string? Workout { get; set; }
}

public sealed class LanePlanSwimmerInputDto
{
    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    /// <summary>null — Unassigned. Порядок в массиве = порядок внутри дорожки.</summary>
    [JsonPropertyName("lane_no")]
    public int? LaneNo { get; set; }

    /// <summary>
    /// Закреплён на этой дорожке (тренер перенёс руками, CF2): «Auto-arrange» его не двигает.
    /// Без дорожки (Unassigned) не учитывается.
    /// </summary>
    [JsonPropertyName("pinned")]
    public bool Pinned { get; set; }
}

/// <summary>
/// План целиком. Дорожек, которых нет в <c>lanes</c>, сервер заводит пустыми (1..lane_count);
/// пловцов, которых нет в <c>swimmers</c>, в плане нет («Not today»).
/// </summary>
public sealed class LanePlanInputDto
{
    [JsonPropertyName("lane_count")]
    public int LaneCount { get; set; }

    /// <summary>Лимит людей на дорожку (перелив по подуровням); null — без лимита.</summary>
    [JsonPropertyName("max_per_lane")]
    public int? MaxPerLane { get; set; }

    /// <summary>
    /// Сколько дорожек «Auto-arrange» оставляет пустыми про запас (0..11). Запас держится всегда,
    /// и когда дорожек не хватает; людям остаётся хотя бы одна.
    /// </summary>
    [JsonPropertyName("reserved_lanes")]
    public int ReservedLanes { get; set; } = LanePlanDefaults.ReservedLanes;

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("lanes")]
    public List<LanePlanLaneInputDto> Lanes { get; set; } = [];

    [JsonPropertyName("swimmers")]
    public List<LanePlanSwimmerInputDto> Swimmers { get; set; } = [];
}

/// <summary>
/// «Distribute»: дорожки с уровнями и кто сегодня пришёл. <c>swimmer_ids</c> null — весь
/// видимый состав. Ничего не сохраняет: раскладку сохраняет следующий PUT плана.
/// </summary>
public sealed class LanePlanDistributeInputDto
{
    [JsonPropertyName("lane_count")]
    public int LaneCount { get; set; }

    /// <summary>Лимит людей на дорожку (перелив по подуровням); null — без лимита.</summary>
    [JsonPropertyName("max_per_lane")]
    public int? MaxPerLane { get; set; }

    [JsonPropertyName("lanes")]
    public List<LanePlanLaneInputDto> Lanes { get; set; } = [];

    [JsonPropertyName("swimmer_ids")]
    public List<int>? SwimmerIds { get; set; }
}

/// <summary>
/// «Auto lanes»: сколько дорожек и кто сегодня пришёл (<c>swimmer_ids</c> null — весь видимый
/// состав). Сервер сам делит дорожки между уровнями (<c>LaneAllocation</c>); <c>reserved_lanes</c>
/// последних дорожек оставляет пустыми всегда. Ничего не сохраняет.
/// </summary>
public sealed class LanePlanAutoLanesInputDto
{
    [JsonPropertyName("lane_count")]
    public int LaneCount { get; set; }

    /// <summary>Лимит людей на дорожку (перелив по подуровням); null — без лимита.</summary>
    [JsonPropertyName("max_per_lane")]
    public int? MaxPerLane { get; set; }

    /// <summary>
    /// Сколько дорожек «Auto-arrange» оставляет пустыми про запас (0..11). Запас держится всегда,
    /// и когда дорожек не хватает; людям остаётся хотя бы одна.
    /// </summary>
    [JsonPropertyName("reserved_lanes")]
    public int ReservedLanes { get; set; } = LanePlanDefaults.ReservedLanes;

    [JsonPropertyName("swimmer_ids")]
    public List<int>? SwimmerIds { get; set; }

    /// <summary>
    /// Закреплённые (CF2): кто на какой дорожке стоит и не должен сдвинуться. Считаются в числе
    /// людей своего уровня, но остаются на своей дорожке; в ответе стоят в её конце с
    /// <c>pinned: true</c>. Не из пришедших или с дорожкой вне 1..lane_count — не закрепление.
    /// </summary>
    [JsonPropertyName("pinned")]
    public List<LanePlanSwimmerInputDto>? Pinned { get; set; }
}

/// <summary>Итог «Auto lanes»: уровень каждой дорожки (1..lane_count) и раскладка людей.</summary>
public sealed class LanePlanAutoLanesDto
{
    /// <summary>Только <c>lane_no</c> и <c>level_id</c>; задания не трогаются.</summary>
    [JsonPropertyName("lanes")]
    public List<LanePlanLaneInputDto> Lanes { get; set; } = [];

    [JsonPropertyName("swimmers")]
    public List<LanePlanSwimmerInputDto> Swimmers { get; set; } = [];
}

/// <summary>Раскладка «Distribute» — в том же виде, что <c>swimmers</c> входа плана.</summary>
public sealed class LanePlanDistributionDto
{
    [JsonPropertyName("swimmers")]
    public List<LanePlanSwimmerInputDto> Swimmers { get; set; } = [];
}
