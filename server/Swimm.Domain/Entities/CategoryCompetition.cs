using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Связка категория ↔ соревнование (M:N).
/// Членство проставляется через импорт / админку; в этом чанке таблица создаётся пустой.
/// </summary>
public class CategoryCompetition
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>FK на категорию.</summary>
    public int CategoryId { get; set; }

    /// <summary>FK на соревнование.</summary>
    public int CompetitionId { get; set; }

    /// <summary>Порядок соревнования внутри категории.</summary>
    public int DisplayOrder { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public Category Category { get; set; } = null!;

    [ForeignKey(nameof(CompetitionId))]
    public Competition Competition { get; set; } = null!;
}
