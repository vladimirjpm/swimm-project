using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт пользовательского самообслуживания групп (8.6) — то, что НЕ совпадает с админским
/// CRUD (<see cref="IHubGroupAdminService"/>): список «моих» групп, лимит/политика создания,
/// со-тренеры. Правка/удаление существующей группы и её участников переиспользует
/// <see cref="IHubGroupAdminService"/> напрямую (логика идентична; авторизация — на уровне
/// контроллера через <see cref="IHubGroupPermissionService"/>).
/// </summary>
public interface IHubGroupUserService
{
    /// <summary>Группы, которыми пользователь владеет или со-управляет.</summary>
    Task<IReadOnlyList<HubGroupAdminRowDto>> GetMineAsync(int userId);

    /// <summary>Может ли пользователь создать ещё одну группу — для UI (кнопка/причина отказа).</summary>
    Task<HubGroupCreateEligibilityDto> GetCreateEligibilityAsync(int userId, bool isAdmin, bool isCoach);

    /// <summary>Создать группу с owner = вызывающий пользователь; enforces policy/лимит внутри.</summary>
    Task<HubGroupSaveResult> CreateAsync(HubGroupInputDto input, int ownerUserId, bool isAdmin, bool isCoach);

    Task<IReadOnlyList<HubGroupManagerDto>> GetManagersAsync(int hubGroupId);

    /// <summary>Назначить со-тренера по точному email. Нет такого пользователя → ошибка.</summary>
    Task<HubGroupMemberSaveResult> AddManagerAsync(int hubGroupId, string email, int grantedByUserId);

    Task<HubGroupMemberSaveResult> RemoveManagerAsync(int hubGroupId, int managerUserId);
}
