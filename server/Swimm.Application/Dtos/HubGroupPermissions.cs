namespace Swimm.Application.Dtos;

/// <summary>
/// Права текущего пользователя на конкретную группу (8.6) — единая точка проверки
/// владелец / админ группы / site-админ, чтобы не размазывать её по хендлерам.
/// </summary>
/// <param name="IsAdmin">Site-админ (роль Admin), не путать с админом группы.</param>
/// <param name="IsOwner">Владелец группы (primary GroupAdmin, создатель).</param>
/// <param name="IsGroupAdmin">Назначенный админ группы (строка Sys_HubGroupAdmins), не владелец.</param>
public sealed record HubGroupPermissions(
    bool Exists,
    bool IsAdmin,
    bool IsOwner,
    bool IsGroupAdmin,
    bool CanEdit,
    bool CanManageAdmins,
    bool CanDelete,
    bool CanChangeOwner)
{
    public static HubGroupPermissions NotFound(bool isAdmin) =>
        new(false, isAdmin, false, false, false, false, false, false);
}
