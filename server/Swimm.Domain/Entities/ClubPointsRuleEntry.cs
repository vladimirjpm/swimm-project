using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Строка шкалы клубных очков: место → количество очков для конкретного правила.
/// </summary>
public class ClubPointsRuleEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>FK на правило начисления очков.</summary>
    public int RuleId { get; set; }

    /// <summary>Место в заплыве (1 = первое).</summary>
    public int Place { get; set; }

    /// <summary>Количество очков за данное место.</summary>
    public int Points { get; set; }

    [ForeignKey(nameof(RuleId))]
    public ClubPointsRule Rule { get; set; } = null!;
}
