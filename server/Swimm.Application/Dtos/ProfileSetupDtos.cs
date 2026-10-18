namespace Swimm.Application.Dtos;

/// <summary>
/// Состояние «Set up profile» для пункта меню и страницы /setup-profile
/// (docs/plans/profile-setup-plan.md). Шаги и счётчик — из <c>ProfileSetupRules</c>.
/// </summary>
public sealed class ProfileSetupDto
{
    /// <summary>
    /// Ответ опроса «Who are you?». Прав не даёт (решение Влада 16.10.2026); «I'm a parent»
    /// добавляет шаг family (хендофф ex3, 17.10.2026).
    /// </summary>
    public ProfileSurveyDto Survey { get; set; } = new();

    /// <summary>Шаги по порядку: me, family (только у родителя), club, group.</summary>
    public IReadOnlyList<ProfileSetupStepDto> Steps { get; set; } = [];

    public int Done { get; set; }
    public int Total { get; set; }

    /// <summary>
    /// Показывать «Set up profile»: не все шаги сделаны и не закрыт Dismiss (или есть новый шаг).
    /// Иначе в меню тихий пункт «My profile».
    /// </summary>
    public bool Visible { get; set; }

    /// <summary>Все шаги сделаны или пропущены — итог «You're all set».</summary>
    public bool Complete { get; set; }

    /// <summary>Несделанные шаги, которых пользователь ещё не видел — «New: …» в меню (хендофф ex2, 3b).</summary>
    public IReadOnlyList<string> NewSteps { get; set; } = [];

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
    /// <summary>me | family | club | group (<c>ProfileSetupRules.Step*</c>).</summary>
    public string Key { get; set; } = "";
    public bool Done { get; set; }

    /// <summary>Засчитан ОТКАЗОМ («Not a swimmer», «No club», «No group»); сделанное делом — false.</summary>
    public bool Skipped { get; set; }

    /// <summary>Id пловца / клуба / группы. У семьи — null (пловцов несколько).</summary>
    public int? TargetId { get; set; }

    /// <summary>
    /// Имя пловца или клуба (иврит по умолчанию, EN фоллбеком), название группы; у семьи — имена
    /// отмеченных пловцов через запятую.
    /// </summary>
    public string? TargetName { get; set; }

    /// <summary>Только у группы — для ссылки.</summary>
    public string? TargetSlug { get; set; }

    /// <summary>Только у группы: active | pending.</summary>
    public string? Status { get; set; }

    /// <summary>
    /// ВСЕ цели шага: каждый избранный клуб, каждая группа (active и pending), каждый пловец семьи,
    /// пловец «это я». Итог рисует строку на каждую (хендофф ex4, решение Р10); поля выше — первая
    /// цель, их читает меню и чек-лист.
    /// </summary>
    public IReadOnlyList<ProfileSetupTargetDto> Targets { get; set; } = [];
}

/// <summary>Одна цель шага: пловец, клуб или группа.</summary>
public sealed class ProfileSetupTargetDto
{
    /// <summary>Id пловца / клуба / группы.</summary>
    public int Id { get; set; }

    /// <summary>Id записи избранного (у пловца и клуба) — для правок из окна на /setup-profile.</summary>
    public int? FavoriteId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Только у группы.</summary>
    public string? Slug { get; set; }

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
