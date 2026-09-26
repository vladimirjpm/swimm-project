using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Уровень пловцов группы (docs/plans/lane-plans-plan.md): свой список у каждой группы,
/// тренер (админ группы) правит его сам; при первом открытии группа получает стандартный набор.
/// ПРИВАТНЫЕ данные — таблица <c>Sys_HubGroupLevels</c>, БЕЗ grant <c>swimm_ro</c>
/// (уровень — оценка тренера, как тренировки в hubgroups-architecture.md §7).
/// </summary>
public class HubGroupLevel
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int HubGroupId { get; set; }

    [ForeignKey(nameof(HubGroupId))]
    public HubGroup? HubGroup { get; set; }

    /// <summary>Порядок: 1 — сильнейший. Сервер ставит 1..N по порядку списка.</summary>
    public int Rank { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Description { get; set; }

    /// <summary>Цвет метки «#rrggbb»; null — клиент красит по рангу.</summary>
    [MaxLength(9)]
    public string? Color { get; set; }
}
