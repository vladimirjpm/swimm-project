using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Категория соревнований (напр. «Main Results», «Masters», «Youth Team»).
/// Членство соревнований — через таблицу CategoryCompetitions (M:N).
/// </summary>
public class Category
{
    /// <summary>Ключ категории Masters: членство в ней определяет Competition.IsMasters.</summary>
    public const string MastersKey = "results-masters";

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Slug-ключ категории, уникальный. Используется в URL и на клиенте.</summary>
    [MaxLength(50)]
    public string Key { get; set; } = string.Empty;

    /// <summary>Человекочитаемое название категории.</summary>
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Порядок отображения (меньше — выше).</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Связанные соревнования (через CategoryCompetitions).</summary>
    public ICollection<CategoryCompetition> Competitions { get; set; } = [];
}
