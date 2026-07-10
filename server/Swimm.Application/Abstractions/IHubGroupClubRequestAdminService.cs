using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт админского рассмотрения заявок на официальный статус группы (фаза 8.7,
/// Admin/HubGroupClubRequests). Одобрение — единая транзакция: HubGroup.IsOfficial/ClubId +
/// site-роль Coach заявителю (если нет) + bump SecurityStamp + email; отклонение — статус + email.
/// </summary>
public interface IHubGroupClubRequestAdminService
{
    Task<IReadOnlyList<HubGroupClubRequestAdminRowDto>> GetAllAsync();

    Task<int> GetPendingCountAsync();

    Task<HubGroupMemberSaveResult> ApproveAsync(int requestId, int adminUserId);

    Task<HubGroupMemberSaveResult> RejectAsync(int requestId, int adminUserId);
}
