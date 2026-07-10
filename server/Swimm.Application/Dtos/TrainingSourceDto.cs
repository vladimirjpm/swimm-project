using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Источник тренировок группы для клиента — форма <c>ResultWrap</c> (results.ts):
/// готовая фича <c>TrainingTable</c> кладёт это в <c>dataSourceSelected</c> и рендерит как раньше
/// делала со статикой JSON. ПРИВАТНОЕ (Sys_-таблицы) — эндпоинт под [Authorize] + проверка прав.
/// </summary>
public sealed class TrainingSourceDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("is_masters")]
    public bool IsMasters { get; set; } = true;

    [JsonPropertyName("results")]
    public List<TrainingRowDto> Results { get; set; } = new();
}

/// <summary>
/// Одна строка тренировки в форме клиентского <c>Result</c> (подмножество полей, которые
/// реально читает <c>TrainingTable</c>), плюс вложенный <c>training</c>.
/// </summary>
public sealed class TrainingRowDto
{
    [JsonPropertyName("country")] public string Country { get; set; } = "IL";
    [JsonPropertyName("competition")] public string Competition { get; set; } = string.Empty;
    [JsonPropertyName("is_masters")] public bool IsMasters { get; set; } = true;
    [JsonPropertyName("age_group")] public string AgeGroup { get; set; } = string.Empty;
    [JsonPropertyName("date")] public string Date { get; set; } = string.Empty;

    [JsonPropertyName("event")] public string Event { get; set; } = string.Empty;
    [JsonPropertyName("event_style_name")] public string EventStyleName { get; set; } = string.Empty;
    [JsonPropertyName("event_style_len")] public string EventStyleLen { get; set; } = string.Empty;
    [JsonPropertyName("event_style_gender")] public string EventStyleGender { get; set; } = string.Empty;
    [JsonPropertyName("event_style_age")] public string EventStyleAge { get; set; } = string.Empty;
    [JsonPropertyName("pool_type")] public string PoolType { get; set; } = string.Empty;

    [JsonPropertyName("position")] public int? Position { get; set; }
    [JsonPropertyName("position_age_group")] public int? PositionAgeGroup { get; set; }
    [JsonPropertyName("heat")] public int Heat { get; set; }
    [JsonPropertyName("lane")] public int Lane { get; set; }

    [JsonPropertyName("swimmer_id")] public int SwimmerId { get; set; }
    [JsonPropertyName("last_name")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name_en")] public string LastNameEn { get; set; } = string.Empty;
    [JsonPropertyName("first_name_en")] public string FirstNameEn { get; set; } = string.Empty;
    [JsonPropertyName("birth_year")] public int BirthYear { get; set; }
    [JsonPropertyName("club")] public string Club { get; set; } = string.Empty;
    [JsonPropertyName("club_en")] public string ClubEn { get; set; } = string.Empty;

    [JsonPropertyName("time")] public string Time { get; set; } = string.Empty;
    [JsonPropertyName("time_split")] public string TimeSplit { get; set; } = string.Empty;
    [JsonPropertyName("time_fail")] public bool TimeFail { get; set; }
    [JsonPropertyName("international_points")] public int InternationalPoints { get; set; }

    [JsonPropertyName("training")] public TrainingInfoDto Training { get; set; } = new();
}

/// <summary>Вложенный <c>training</c> — форма клиентского <c>TrainingInfo</c>.</summary>
public sealed class TrainingInfoDto
{
    [JsonPropertyName("trainingId")] public long TrainingId { get; set; }
    [JsonPropertyName("trainingName")] public string TrainingName { get; set; } = string.Empty;
    [JsonPropertyName("set")] public int Set { get; set; }
    [JsonPropertyName("order")] public int Order { get; set; }
    [JsonPropertyName("interval")] public int? Interval { get; set; }
    [JsonPropertyName("intensity")] public string? Intensity { get; set; }
    [JsonPropertyName("expected_time")] public string? ExpectedTime { get; set; }

    [JsonPropertyName("isPaddles")] public bool IsPaddles { get; set; }
    [JsonPropertyName("isBuoy")] public bool IsBuoy { get; set; }
    // Остальной инвентарь в данных Дельфина отсутствует — отдаём false для полноты формы TrainingInfo.
    [JsonPropertyName("isFins")] public bool IsFins { get; set; }
    [JsonPropertyName("isSnorkel")] public bool IsSnorkel { get; set; }
    [JsonPropertyName("isBoard")] public bool IsBoard { get; set; }
}
