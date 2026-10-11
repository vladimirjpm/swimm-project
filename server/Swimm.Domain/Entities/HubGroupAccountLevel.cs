namespace Swimm.Domain.Entities;

/// <summary>
/// Уровень АККАУНТА-участника в группе — для того, кто стоит на дорожке сам, без пловца (его нет
/// в loglig; решение Влада 28.09.2026, docs/plans/entity-hero-roles-plan.md §5). Пара к
/// <see cref="HubGroupSwimmerLevel"/>: тот ключуется пловцом, этот — аккаунтом. Какой из двух
/// действует для человека, решает чтение (сначала его пловец, потом аккаунт).
/// ПРИВАТНЫЕ данные — <c>Sys_HubGroupAccountLevels</c>, БЕЗ grant <c>swimm_ro</c>.
/// </summary>
public class HubGroupAccountLevel
{
    public int HubGroupId { get; set; }

    public HubGroup? HubGroup { get; set; }

    public int UserId { get; set; }

    public AppUser? User { get; set; }

    /// <summary>Уровень ЭТОЙ ЖЕ группы — составной FK <c>(HubGroupId, LevelId)</c>, как у пловца.</summary>
    public int LevelId { get; set; }

    public HubGroupLevel? Level { get; set; }
}
