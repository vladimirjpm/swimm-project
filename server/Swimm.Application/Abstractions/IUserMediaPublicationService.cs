using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Публикации личного медиа (<c>UserMedia</c>) в группы — этап 2 модели видимости
/// (память media-visibility-model): подача владельцем, одобрение/снятие админом группы.
/// Правила подачи: податель — владелец медиа И активный user-член группы (или админ/владелец
/// группы, тогда сразу approved); пловец медиа — в ростере группы (HubGroupMembers).
/// Подтверждение личности «родитель↔пловец» сознательно НЕ требуется — модерация по контенту.
/// </summary>
public interface IUserMediaPublicationService
{
    /// <summary>
    /// Подать медиа в группу (level: members|public). Повторная подача после reject
    /// возвращает ту же строку в pending. (Success=false, Error) — при нарушении правил.
    /// </summary>
    Task<(bool Success, string? Error, UserMediaPublicationDto? Publication)> SubmitAsync(
        int ownerUserId, int mediaId, SubmitPublicationRequest request, bool isGroupPrivileged);

    /// <summary>Отозвать публикацию своего медиа из группы (любой статус). false — нет такой.</summary>
    Task<bool> WithdrawAsync(int ownerUserId, int mediaId, int hubGroupId);

    /// <summary>Публикации всех медиа владельца (для списка «Мои ссылки»/страницы медиа).</summary>
    Task<List<UserMediaPublicationDto>> GetForOwnerAsync(int ownerUserId);

    /// <summary>
    /// Куда владелец может подать это медиа: группы, где он активный user-член И пловец
    /// медиа в ростере. Для честного селектора в UI (не предлагать группы, где сервер
    /// всё равно откажет). Чужое/несуществующее медиа → пустой список.
    /// </summary>
    Task<List<PublishTargetDto>> GetPublishTargetsAsync(int ownerUserId, int mediaId);

    /// <summary>Inbox модерации группы: pending + approved (для снятия). Авторизацию решает контроллер.</summary>
    Task<List<GroupPublicationInboxItemDto>> GetForGroupAsync(int hubGroupId);

    /// <summary>
    /// Сводный inbox модерации (страница My media → Moderation): заявки ВСЕХ статусов по
    /// группам, где userId владелец или админ; isSiteAdmin=true — по всем группам. Pending первыми.
    /// </summary>
    Task<List<GroupPublicationInboxItemDto>> GetModerationFeedAsync(int userId, bool isSiteAdmin);

    /// <summary>
    /// Одобренные публикации группы уровня level (members|public) — для отображения на
    /// странице группы. Авторизацию members-уровня решает контроллер; не подмешивается
    /// в HubGroupMedia-галереи (другая природа id, снятие — через DecideAsync, не Delete).
    /// </summary>
    Task<List<GroupPublicationInboxItemDto>> GetApprovedForGroupAsync(int hubGroupId, string level);

    /// <summary>
    /// Решение админа группы: approve=true → approved, approve=false → rejected
    /// (для approved это «снять с публикации»). false — заявка не найдена/не этой группы.
    /// </summary>
    Task<bool> DecideAsync(int hubGroupId, int publicationId, bool approve, int decidedByUserId);

    /// <summary>
    /// Медиа с привязкой к заплыву, видимое зрителю userId (null = аноним) в рамках
    /// соревнования competitionId, всех дней события eventId или group-режима groupSlug
    /// (заплывы пловцов из ростера группы): своё (любое) + approved public (всем) +
    /// approved members (только активным членам той группы). Для иконок видео в таблице.
    /// </summary>
    Task<List<VisibleResultMediaDto>> GetVisibleForResultsAsync(
        int? competitionId, int? eventId, string? groupSlug, int? userId);
}
