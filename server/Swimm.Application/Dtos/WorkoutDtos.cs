using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

// Workout занятия (docs/plans/trainings-coach-flow-plan.md, §5.5, CF6): один документ при плане
// дорожек, публикуется вместе с ним. Строка — упражнение, колонки — уровни группы: у уровня своё
// время выхода и, если надо, своё число повторов. snake_case, как у всей ручки lane-plans.
// Итоговые метры сервер не хранит и не считает — их считает клиент.

/// <summary>Workout занятия: заметка и сеты по порядку.</summary>
public sealed class WorkoutDto
{
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("sets")]
    public List<WorkoutSetDto> Sets { get; set; } = [];
}

/// <summary>Сет: название, сколько раз повторить весь сет и кому он (null — всем уровням).</summary>
public sealed class WorkoutSetDto
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Повтор сета целиком («2X» со скобкой на листе); 1 — один раз.</summary>
    [JsonPropertyName("repeat")]
    public int Repeat { get; set; } = 1;

    /// <summary>null — сет плывут все уровни; список — только названные (галочки над колонками).</summary>
    [JsonPropertyName("level_ids")]
    public List<int>? LevelIds { get; set; }

    /// <summary>
    /// Главный сет (תרגיל מרכזי) — его отмечает тренер галочкой «Main set»; главных может быть несколько.
    /// При <c>false</c> в JSON не пишется: старые документы без поля читаются как false и не меняются
    /// от пересохранения (docs/plans/workout-print-plan.md, §3.1).
    /// </summary>
    [JsonPropertyName("main")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Main { get; set; }

    [JsonPropertyName("lines")]
    public List<WorkoutLineDto> Lines { get; set; } = [];
}

/// <summary>Строка сета: «4 × 100 free 2-3V» и время выхода по уровням.</summary>
public sealed class WorkoutLineDto
{
    /// <summary>Повторов; 1 — одна дистанция.</summary>
    [JsonPropertyName("reps")]
    public int Reps { get; set; } = 1;

    /// <summary>Метров в одном повторе; null — строка без дистанции (отдых, пояснение).</summary>
    [JsonPropertyName("distance")]
    public int? Distance { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Зона интенсивности как на листе: «2V», «2-3V».</summary>
    [JsonPropertyName("intensity")]
    public string? Intensity { get; set; }

    /// <summary>Ключ — id уровня группы (<c>HubGroupLevel.Id</c>). У подуровней своих времён нет.</summary>
    [JsonPropertyName("per_level")]
    public Dictionary<int, WorkoutCellDto>? PerLevel { get; set; }
}

/// <summary>Ячейка уровня: время выхода строкой и своё число повторов (перекрывает строку).</summary>
public sealed class WorkoutCellDto
{
    /// <summary>Строкой: на листе встречаются «:35», «1:40», «2:30-40», «rest :20».</summary>
    [JsonPropertyName("send_off")]
    public string? SendOff { get; set; }

    /// <summary>Своё число повторов уровня («3 או 100X4»); null — как у строки.</summary>
    [JsonPropertyName("reps")]
    public int? Reps { get; set; }
}

/// <summary>Тело <c>PUT …/lane-plans/{date}/workout</c>: <c>workout</c> null или пустой — стереть.</summary>
public sealed class WorkoutInputDto
{
    [JsonPropertyName("workout")]
    public WorkoutDto? Workout { get; set; }
}
