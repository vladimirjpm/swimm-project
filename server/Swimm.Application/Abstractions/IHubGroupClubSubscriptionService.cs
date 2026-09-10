using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Подписка группы на клуб и пересборка состава из пловцов клуба
/// (docs/plans/hubgroup-club-subscription-plan.md П2). Правила — <c>HubGroupClubRules</c>.
///
/// Права здесь НЕ проверяются: вызывающий уже прошёл <see cref="IHubGroupPermissionService"/>
/// (CanEdit) — так же, как остальной CRUD групп. Все мутации сбрасывают публичный кэш групп.
/// </summary>
public interface IHubGroupClubSubscriptionService
{
    /// <summary>Текущая подписка группы; null — группа ни на кого не подписана.</summary>
    Task<HubGroupClubSubscriptionDto?> GetAsync(int hubGroupId);

    /// <summary>
    /// Предпросмотр подписки на клуб: сколько пловцов придёт, предупреждение про официальную
    /// группу и подсказка «вступить в существующую». Ничего не пишет. null — клуба нет.
    /// </summary>
    Task<HubGroupClubSubscriptionPreviewDto?> PreviewAsync(int hubGroupId, int clubId);

    /// <summary>
    /// Подписать группу на клуб и сразу пересобрать состав. Подписка на другой клуб
    /// ЗАМЕНЯЕТ прежнюю (одна на группу): пловцы прежнего клуба уходят, ручные остаются.
    /// Склеенный клуб подменяется каноническим.
    /// </summary>
    Task<HubGroupClubSubscribeResult> SubscribeAsync(int hubGroupId, int clubId, int? userId);

    /// <summary>
    /// Снять подписку: уходят все клубные строки (и скрытые), ручные остаются. null — подписки не было.
    /// </summary>
    Task<HubGroupClubSyncResult?> UnsubscribeAsync(int hubGroupId);

    /// <summary>Пересобрать состав одной группы (без подписки — убрать клубные строки).</summary>
    Task<HubGroupClubSyncResult> SyncGroupAsync(int hubGroupId);

    /// <summary>Пересобрать группы, подписанные на эти клубы (после импорта — клубы импорта).</summary>
    Task<HubGroupClubSyncResult> SyncClubsAsync(IReadOnlyCollection<int> clubIds);

    /// <summary>Пересобрать все подписанные группы (CLI <c>--hubgroup-club-sync</c>).</summary>
    Task<HubGroupClubSyncResult> SyncAllAsync();

    /// <summary>
    /// Скрыть / вернуть клубного пловца. Скрытый остаётся строкой — пересборка его не
    /// возвращает, — но ни один читатель состава его не видит. Ручного скрыть нельзя.
    /// </summary>
    Task<HubGroupMemberSaveResult> SetExcludedAsync(int hubGroupId, int memberId, bool excluded);
}
