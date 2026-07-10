namespace Swimm.Application.Dtos;

/// <summary>
/// Права текущего пользователя на конкретную группу (8.6) — единая точка проверки
/// владелец/со-тренер/админ, чтобы не размазывать её по хендлерам.
/// </summary>
public sealed record HubGroupPermissions(
    bool Exists,
    bool IsAdmin,
    bool IsOwner,
    bool IsCoManager,
    bool CanEdit,
    bool CanManageCoTrainers,
    bool CanDelete,
    bool CanChangeOwner)
{
    public static HubGroupPermissions NotFound(bool isAdmin) =>
        new(false, isAdmin, false, false, false, false, false, false);
}
