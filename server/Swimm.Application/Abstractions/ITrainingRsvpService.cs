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
    /// <summary>
    /// Ответы на занятие; null — такого занятия нет (не в расписании, не разовое «Ask who's
    /// coming») или оно вне окна.
    /// </summary>
    Task<TrainingRsvpDto?> GetAsync(int hubGroupId, string sessionKey, int viewerUserId, bool isManager,
        DateTime? nowLocal = null);

    /// <summary>Поставить / снять ответ (свой или, управляющему, за участника).</summary>
    Task<TrainingRsvpSaveResult> SetAsync(int hubGroupId, string sessionKey, int actorUserId, bool isManager,
        TrainingRsvpInputDto input, DateTime? nowLocal = null);

    /// <summary>
    /// Ближайшие занятия для вида Next (Р9): до трёх не отменённых по расписанию и разовым и
    /// отменённые между ними. Приватный список — в кэшируемую страницу группы разовые и отмены не
    /// попадают. Null — группы нет.
    /// </summary>
    Task<List<NextTrainingDto>?> GetNextAsync(int hubGroupId, DateTime? nowLocal = null);

    /// <summary>
    /// Прошедшие занятия с данными — ответами или планом дорожек — за
    /// <c>TrainingRsvpRules.PastDaysBack</c> дней, сначала новые (CF9). Право (управляющий)
    /// проверяет контроллер. Null — группы нет.
    /// </summary>
    Task<List<PastTrainingDto>?> GetPastAsync(int hubGroupId, DateTime? nowLocal = null);

    /// <summary>
    /// Список «Past» УЧАСТНИКА (решение Влада 26.11.2026): прошедшие занятия с ОПУБЛИКОВАННЫМ планом
    /// дорожек за те же <c>TrainingRsvpRules.PastDaysBack</c> дней, сначала новые; без счётчиков
    /// ответов (<c>Yes / Maybe / No</c> — null). Черновики и занятия только с ответами — не здесь.
    /// Null — группы нет.
    /// </summary>
    Task<List<PastTrainingDto>?> GetMemberPastAsync(int hubGroupId, DateTime? nowLocal = null);

    /// <summary>
    /// Отменить занятие (Р9) — «в этот вторник тренировки нет». Право (управляющий) проверяет
    /// контроллер. Занятие должно существовать и лежать в окне ответов (400 «There is no such
    /// training.»); повторная отмена — успех, причина обновляется. Ответы не удаляются.
    /// </summary>
    Task<TrainingRsvpSaveResult> CancelAsync(int hubGroupId, string sessionKey, int actorUserId, string? reason,
        DateTime? nowLocal = null);

    /// <summary>Вернуть отменённое занятие; не отменённое — тоже успех (идемпотентно).</summary>
    Task<TrainingRsvpSaveResult> RestoreAsync(int hubGroupId, string sessionKey, int actorUserId,
        DateTime? nowLocal = null);
}
