using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Уровни пловцов группы (docs/plans/lane-plans-plan.md, L1). Правила — <c>HubGroupLevelRules</c>.
///
/// Права здесь НЕ проверяются: вызывающий уже прошёл <see cref="IHubGroupPermissionService"/>
/// (CanEdit) — как остальной CRUD групп. Данные приватные (Sys_), в публичный кэш групп не
/// входят — сбрасывать нечего.
/// </summary>
public interface IHubGroupLevelService
{
    /// <summary>
    /// Уровни и состав с уровнями. У группы без уровней сначала заводит стандартный набор
    /// (решение Влада: «при первом открытии»). null — группы нет.
    /// </summary>
    Task<HubGroupLevelsDto?> GetAsync(int hubGroupId);

    /// <summary>
    /// Сохранить список уровней целиком: порядок = ранг, отсутствующие удаляются вместе с
    /// назначениями пловцов (те становятся «без уровня»).
    /// </summary>
    Task<HubGroupMemberSaveResult> SaveLevelsAsync(int hubGroupId, HubGroupLevelsInputDto input);

    /// <summary>
    /// Поставить/снять уровень пловцу. Пловец должен быть в видимом составе группы, уровень —
    /// из этой же группы; null снимает.
    /// </summary>
    Task<HubGroupMemberSaveResult> SetSwimmerLevelAsync(int hubGroupId, int swimmerId, int? levelId);
}
