using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт админского рассмотрения заявок на официальный статус группы (фаза 8.7,
/// Admin/HubGroupClubRequests). Одобрение — единая транзакция: HubGroup.IsOfficial/ClubId +
/// site-роль Coach заявителю (если нет) + bump SecurityStamp + « · community» у групп, чьё имя
/// совпало с клубом (П4); после неё — подписка на клуб, email и аудит. Отклонение — статус + email.
/// </summary>
public interface IHubGroupClubRequestAdminService
{
    /// <summary>Все заявки; у pending — последствия одобрения (<see cref="HubGroupClubRequestAdminRowDto.ApproveImpact"/>).</summary>
    Task<IReadOnlyList<HubGroupClubRequestAdminRowDto>> GetAllAsync();

    Task<int> GetPendingCountAsync();

    Task<HubGroupMemberSaveResult> ApproveAsync(int requestId, int adminUserId);

    Task<HubGroupMemberSaveResult> RejectAsync(int requestId, int adminUserId);
}
