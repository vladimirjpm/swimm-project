using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Справочник соревнований.
/// </summary>
public class Competition
{
  [Key]
  [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
  public int Id { get; set; }

  /// <summary>Название соревнования</summary>
  [MaxLength(300)]
  public string Name { get; set; } = string.Empty;

  /// <summary>Страна проведения (ISR и т. д.)</summary>
  [MaxLength(10)]
  public string Country { get; set; } = string.Empty;

  /// <summary>Дата соревнования в формате dd/MM/yyyy</summary>
  [MaxLength(20)]
  public string Date { get; set; } = string.Empty;

  /// <summary>Тип бассейна: 25m / 50m</summary>
  [MaxLength(5)]
  public string PoolType { get; set; } = string.Empty;

  public bool IsMasters { get; set; }

  public bool IsAward { get; set; }

  /// <summary>
  /// Признак: отображать объединённую таблицу всех результатов (без разбивки по полу/возрасту).
  /// </summary>
  public bool ShowCombineAllResults { get; set; }
}
