namespace Swimm.Domain.Entities;

/// <summary>
/// Уровень пловца в группе. Ключ — <c>(HubGroupId, SwimmerId)</c>, НЕ <see cref="HubGroupMember"/>.Id:
/// клубных пловцов пересборка подписки удаляет и заводит заново, и уровень потерялся бы вместе
/// со строкой состава (docs/plans/lane-plans-plan.md). Нет строки — «без уровня».
/// ПРИВАТНЫЕ данные — <c>Sys_HubGroupSwimmerLevels</c>, БЕЗ grant <c>swimm_ro</c>.
/// </summary>
public class HubGroupSwimmerLevel
{
    public int HubGroupId { get; set; }

    public HubGroup? HubGroup { get; set; }

    public int SwimmerId { get; set; }

    public Swimmer? Swimmer { get; set; }

    /// <summary>
    /// Уровень ЭТОЙ ЖЕ группы: внешний ключ составной <c>(HubGroupId, LevelId)</c> →
    /// <c>(HubGroupId, Id)</c> уровня, так что чужой уровень база не примет.
    /// </summary>
    public int LevelId { get; set; }

    public HubGroupLevel? Level { get; set; }
}
