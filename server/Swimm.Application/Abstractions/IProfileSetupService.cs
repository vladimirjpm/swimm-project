using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// «Set up profile» (docs/plans/profile-setup-plan.md): три шага (это я, клуб, группа), опрос
/// «Who are you?» и Dismiss. Только своё: всё — по id текущего пользователя.
/// </summary>
public interface IProfileSetupService
{
    /// <summary>Состояние для меню и страницы. null — пользователя нет.</summary>
    Task<ProfileSetupDto?> GetAsync(int userId);

    /// <summary>Сохранить ответ опроса (замена целиком) и время ответа. false — пользователя нет.</summary>
    Task<bool> SaveSurveyAsync(int userId, ProfileSurveyRequest answer);

    /// <summary>Dismiss (true) или вернуть пункт (false). false в ответе — пользователя нет.</summary>
    Task<bool> SetDismissedAsync(int userId, bool dismissed);

    /// <summary>
    /// Отказ от шага (<c>ProfileSetupRules.Step*</c>) или его отмена. Ключ проверяет вызывающий
    /// (<c>ProfileSetupRules.IsStepKey</c>). false — пользователя нет.
    /// </summary>
    Task<bool> SetStepSkippedAsync(int userId, string stepKey, bool skipped);
}
