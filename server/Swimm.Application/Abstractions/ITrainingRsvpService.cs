using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Ответы «иду / не уверен / не приду» на занятие группы (docs/plans/entity-hero-roles-plan.md,
/// Ш2). Правила окна и ключа занятия — <c>TrainingRsvpRules</c>.
///
/// Права (управляющий ли зритель — <c>HubGroupPermissions.CanEdit</c>) решает контроллер и
/// передаёт флагом; членство зрителя сервис проверяет сам — от него зависят и кнопки, и то,
/// за кого можно ответить. <paramref name="nowLocal"/> — «сейчас» по Израилю; не задан —
/// текущее (задают тесты).
/// </summary>
public interface ITrainingRsvpService
{
    /// <summary>Ответы на занятие; null — такого занятия нет (не в расписании или вне окна).</summary>
    Task<TrainingRsvpDto?> GetAsync(int hubGroupId, string sessionKey, int viewerUserId, bool isManager,
        DateTime? nowLocal = null);

    /// <summary>Поставить / снять ответ (свой или, управляющему, за участника).</summary>
    Task<TrainingRsvpSaveResult> SetAsync(int hubGroupId, string sessionKey, int actorUserId, bool isManager,
        TrainingRsvpInputDto input, DateTime? nowLocal = null);
}
