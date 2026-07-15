using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт пользовательского самообслуживания групп (8.6) — то, что НЕ совпадает с админским
/// CRUD (<see cref="IHubGroupAdminService"/>): список «моих» групп, лимит/политика создания,
/// админы группы. Правка/удаление существующей группы и её участников переиспользует
/// <see cref="IHubGroupAdminService"/> напрямую (логика идентична; авторизация — на уровне
/// контроллера через <see cref="IHubGroupPermissionService"/>).
/// </summary>
public interface IHubGroupUserService
{
    /// <summary>Группы, которыми пользователь владеет или админит.</summary>
    Task<IReadOnlyList<HubGroupAdminRowDto>> GetMineAsync(int userId);

    /// <summary>Может ли пользователь создать ещё одну группу — для UI (кнопка/причина отказа).</summary>
    Task<HubGroupCreateEligibilityDto> GetCreateEligibilityAsync(int userId, bool isAdmin, bool isCoach);

    /// <summary>Создать группу с owner = вызывающий пользователь; enforces policy/лимит внутри.</summary>
    Task<HubGroupSaveResult> CreateAsync(HubGroupInputDto input, int ownerUserId, bool isAdmin, bool isCoach);

    /// <summary>
    /// Правка существующей группы пользователем. В отличие от админской <see cref="IHubGroupAdminService.UpdateAsync"/>
    /// не меняет привязку к клубу (ClubId ведётся только админом/одобрением заявки).
    /// Авторизация — на уровне контроллера.
    /// </summary>
    Task<HubGroupSaveResult> UpdateAsync(int id, HubGroupInputDto input);

    Task<IReadOnlyList<HubGroupAdminMemberDto>> GetAdminsAsync(int hubGroupId);

    /// <summary>Назначить админа группы по точному email. Нет такого пользователя → ошибка.</summary>
    Task<HubGroupMemberSaveResult> AddAdminAsync(int hubGroupId, string email, int grantedByUserId);

    Task<HubGroupMemberSaveResult> RemoveAdminAsync(int hubGroupId, int adminUserId);

    // ── Участники-аккаунты (приватный состав из юзеров) ──────────────────────

    /// <summary>
    /// Добавить участника-аккаунт по email (владелец/админ). AddedByUserId проставляется.
    /// swimmerId/note — опциональный ярлык «за какого пловца» (напр. родитель), см. <see cref="SetUserMemberLabelAsync"/>.
    /// </summary>
    Task<HubGroupMemberSaveResult> AddUserMemberAsync(int hubGroupId, string email, int addedByUserId, int? swimmerId = null, string? note = null);

    /// <summary>Убрать участника-аккаунт (владелец/админ; либо самовыход через LeaveAsync).</summary>
    Task<HubGroupMemberSaveResult> RemoveUserMemberAsync(int hubGroupId, int userId);

    /// <summary>
    /// Проставить/снять ярлык «родитель пловца» у уже существующего участника-аккаунта —
    /// чисто отображение в списке состава, прав не меняет. swimmerId=null снимает ярлык.
    /// </summary>
    Task<HubGroupMemberSaveResult> SetUserMemberLabelAsync(int hubGroupId, int userId, int? swimmerId, string? note);

    /// <summary>
    /// Самозапись: вступить в публичную/видимую группу. При JoinPolicy=open — сразу active,
    /// при approval — заявка pending (активирует владелец/админ через ApproveUserMemberAsync).
    /// </summary>
    Task<HubGroupMemberSaveResult> JoinAsync(int hubGroupId, int userId);

    /// <summary>Одобрить заявку участника-аккаунта: pending → active. Авторизация (CanEdit) — в контроллере.</summary>
    Task<HubGroupMemberSaveResult> ApproveUserMemberAsync(int hubGroupId, int userId);

    /// <summary>Самовыход из группы.</summary>
    Task<HubGroupMemberSaveResult> LeaveAsync(int hubGroupId, int userId);

    /// <summary>Группы, в которые пользователь вступил как участник-аккаунт (список «участвую»).</summary>
    Task<IReadOnlyList<JoinedHubGroupDto>> GetJoinedAsync(int userId);

    /// <summary>Последняя заявка на официальный статус (любого статуса) для группы — для UI «Моя группа». null — заявок не было.</summary>
    Task<MyHubGroupClubRequestDto?> GetClubRequestAsync(int hubGroupId);

    /// <summary>
    /// Подать заявку на официальный статус (владелец группы). Отказ — если группа уже
    /// официальная, уже есть pending-заявка, или у клуба уже есть официальная группа.
    /// </summary>
    Task<HubGroupMemberSaveResult> SubmitClubRequestAsync(int hubGroupId, int userId, HubGroupClubRequestInputDto input);
}
