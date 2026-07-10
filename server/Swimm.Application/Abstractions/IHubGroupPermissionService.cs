using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт единой проверки прав на группу (владелец/со-тренер/админ) — см. таблицу прав
/// в docs/tasks/hubgroups-phase8.5-8.6.md, Часть B.
/// </summary>
public interface IHubGroupPermissionService
{
    /// <summary>isAdmin — берётся из ClaimsPrincipal (User.IsInRole("Admin")) на уровне контроллера.</summary>
    Task<HubGroupPermissions> GetPermissionsAsync(int hubGroupId, int userId, bool isAdmin);
}
