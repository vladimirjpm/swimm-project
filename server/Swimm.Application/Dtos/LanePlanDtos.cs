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

    /// <summary>Пловца уже нет в составе группы — в плане он остался (план — снимок).</summary>
    [JsonPropertyName("left_group")]
    public bool LeftGroup { get; set; }
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

/// <summary>«Мой» пловец плана: <c>me</c> — сам зритель, <c>family</c> — за кого он смотрит.</summary>
public sealed class LanePlanMySwimmerDto
{
    [JsonPropertyName("swimmer_id")]
    public int SwimmerId { get; set; }

    /// <summary>me | family.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "me";
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
}

/// <summary>
/// План целиком. Дорожек, которых нет в <c>lanes</c>, сервер заводит пустыми (1..lane_count);
/// пловцов, которых нет в <c>swimmers</c>, в плане нет («Not today»).
/// </summary>
public sealed class LanePlanInputDto
{
    [JsonPropertyName("lane_count")]
    public int LaneCount { get; set; }

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

    [JsonPropertyName("lanes")]
    public List<LanePlanLaneInputDto> Lanes { get; set; } = [];

    [JsonPropertyName("swimmer_ids")]
    public List<int>? SwimmerIds { get; set; }
}

/// <summary>
/// «Auto lanes»: сколько дорожек и кто сегодня пришёл (<c>swimmer_ids</c> null — весь видимый
/// состав). Сервер сам делит дорожки между уровнями (<c>LaneAllocation</c>). Ничего не сохраняет.
/// </summary>
public sealed class LanePlanAutoLanesInputDto
{
    [JsonPropertyName("lane_count")]
    public int LaneCount { get; set; }

    [JsonPropertyName("swimmer_ids")]
    public List<int>? SwimmerIds { get; set; }
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
