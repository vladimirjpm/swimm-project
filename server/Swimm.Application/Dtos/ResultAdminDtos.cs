namespace Swimm.Application.Dtos;

/// <summary>
/// Данные одного результата для формы ручной правки Admin/Results/Edit (фаза 7.2 B).
/// Контекст (соревнование/пловец/клуб/стиль) — только для показа; правятся скалярные поля
/// заплыва + при необходимости переназначение пловца/клуба по Id.
/// </summary>
public sealed class ResultEditDto
{
    public long Id { get; set; }

    // Контекст (read-only в форме)
    public int CompetitionId { get; set; }
    public string CompetitionName { get; set; } = "";
    public DateTime CompetitionDate { get; set; }
    public string StyleName { get; set; } = "";

    // Переназначаемые ссылки (+ текущее отображение)
    public int SwimmerId { get; set; }
    public string SwimmerName { get; set; } = "";
    public int ClubId { get; set; }
    public string ClubName { get; set; } = "";

    // Правимые поля
    public string Distance { get; set; } = "";
    public string Gender { get; set; } = "";
    public string AgeGroup { get; set; } = "";
    public string EventStyleAge { get; set; } = "";
    public int? Position { get; set; }
    public int? PositionAgeGroup { get; set; }
    public int Heat { get; set; }
    public int Lane { get; set; }
    public string TimeText { get; set; } = "";
    public bool TimeFail { get; set; }
    public string? TimeFailNote { get; set; }
    public int InternationalPoints { get; set; }
    public string? Note { get; set; }
}

/// <summary>Входные данные ручной правки результата.</summary>
public sealed class ResultEditInputDto
{
    public int SwimmerId { get; set; }
    public int ClubId { get; set; }
    public string Distance { get; set; } = "";
    public string Gender { get; set; } = "";
    public string AgeGroup { get; set; } = "";
    public string EventStyleAge { get; set; } = "";
    public int? Position { get; set; }
    public int? PositionAgeGroup { get; set; }
    public int Heat { get; set; }
    public int Lane { get; set; }
    /// <summary>Время как «1:02.34» / «58.21»; пусто — снять время (null).</summary>
    public string? TimeText { get; set; }
    public bool TimeFail { get; set; }
    public string? TimeFailNote { get; set; }
    public int InternationalPoints { get; set; }
    public string? Note { get; set; }
}

/// <summary>Результат мутации: успех + сообщение об ошибке для формы.</summary>
public sealed record ResultSaveResult(bool Success, string? Error)
{
    public static ResultSaveResult Ok() => new(true, null);
    public static ResultSaveResult Fail(string error) => new(false, error);
}
