using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Правило начисления клубных очков: версия, область применения, шкала мест.
/// </summary>
public class PointRuleClubs
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Идентификатор версии правила, напр. "2025.01" или "2025.01-masters".</summary>
    [MaxLength(50)]
    public string Version { get; set; } = string.Empty;

    /// <summary>Дата вступления правила в силу.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Человекочитаемое описание правила.</summary>
    [MaxLength(300)]
    public string? Description { get; set; }

    /// <summary>Область применения: "all", "masters", "non-masters".</summary>
    [MaxLength(20)]
    public string Scope { get; set; } = string.Empty;

    /// <summary>Очки по умолчанию для мест за пределами шкалы.</summary>
    public int DefaultPoints { get; set; }

    /// <summary>Максимальное место, за которое начисляются очки (null = без ограничения).</summary>
    public int? MaxScoringPlace { get; set; }

    /// <summary>Строки шкалы: место → очки.</summary>
    public ICollection<PointRuleClubsEntry> Entries { get; set; } = [];
}
