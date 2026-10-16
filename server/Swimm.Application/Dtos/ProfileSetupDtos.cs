namespace Swimm.Application.Dtos;

/// <summary>
/// Состояние «Set up profile» для пункта меню и страницы /setup-profile
/// (docs/plans/profile-setup-plan.md). Шаги и счётчик — из <c>ProfileSetupRules</c>.
/// </summary>
public sealed class ProfileSetupDto
{
    /// <summary>Ответ опроса «Who are you?». Ни на что не влияет (решение Влада 16.10.2026).</summary>
    public ProfileSurveyDto Survey { get; set; } = new();

    /// <summary>Три шага в фиксированном порядке: me, club, group.</summary>
    public IReadOnlyList<ProfileSetupStepDto> Steps { get; set; } = [];

    public int Done { get; set; }
    public int Total { get; set; }

    /// <summary>Показывать ли пункт меню: не закрыт Dismiss и не все шаги сделаны.</summary>
    public bool Visible { get; set; }

    public bool Dismissed { get; set; }
}

/// <summary>Ответ опроса — четыре галочки и когда последний раз менялся (null — не отвечал).</summary>
public sealed class ProfileSurveyDto
{
    public bool Swimmer { get; set; }
    public bool Parent { get; set; }
    public bool Coach { get; set; }
    public bool Watcher { get; set; }
    public DateTime? AnsweredAt { get; set; }
}

/// <summary>
/// Шаг и то, чем он закрыт: пловец «это я», первый избранный клуб, группа (active важнее pending).
/// У несделанного шага поля цели пустые.
/// </summary>
public sealed class ProfileSetupStepDto
{
    /// <summary>me | club | group (<c>ProfileSetupRules.Step*</c>).</summary>
    public string Key { get; set; } = "";
    public bool Done { get; set; }

    /// <summary>Засчитан ОТКАЗОМ («Not a swimmer», «No club», «No group»); сделанное делом — false.</summary>
    public bool Skipped { get; set; }

    /// <summary>Id пловца / клуба / группы.</summary>
    public int? TargetId { get; set; }

    /// <summary>Имя пловца или клуба (иврит по умолчанию, EN фоллбеком), название группы.</summary>
    public string? TargetName { get; set; }

    /// <summary>Только у группы — для ссылки.</summary>
    public string? TargetSlug { get; set; }

    /// <summary>Только у группы: active | pending.</summary>
    public string? Status { get; set; }
}

/// <summary>Тело <c>PUT /api/me/profile-setup/roles</c> — все четыре ответа разом (замена целиком).</summary>
public sealed class ProfileSurveyRequest
{
    public bool Swimmer { get; set; }
    public bool Parent { get; set; }
    public bool Coach { get; set; }
    public bool Watcher { get; set; }
}
