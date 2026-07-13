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

  /// <summary>Страна проведения — FK на справочник Countries (как у Swimmer/Club/HubGroup).
  /// null — страна не задана (старые/ручные соревнования).</summary>
  public int? CountryId { get; set; }

  [ForeignKey(nameof(CountryId))]
  public Country? Country { get; set; }

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

  // ── Многодневные соревнования ──────────────────────────────────────────────

  /// <summary>
  /// Ссылка на событие, если этот день — часть многодневного соревнования.
  /// null — обычное однодневное соревнование.
  /// </summary>
  public int? EventId { get; set; }

  [ForeignKey(nameof(EventId))]
  public CompetitionEvent? Event { get; set; }

  /// <summary>Порядковый номер дня внутри события (1..N). null для однодневных.</summary>
  public int? DayNumber { get; set; }

  /// <summary>
  /// Оригинальный заголовок соревнования из файла этого дня.
  /// Для дней события <see cref="Name"/> = общее имя события, а специфичный заголовок дня тут.
  /// </summary>
  [MaxLength(300)]
  public string? SubName { get; set; }

  // ── Внешний ID соревнования у организатора (loglig.com и т.п.) ─────────────

  /// <summary>
  /// ID соревнования во внешней системе организатора (например loglig.com competitionId).
  /// null — у старых/ручных соревнований этого ID пока нет.
  /// Связь с <see cref="CompetitionResultUrl"/> идёт по этому полю (не по <see cref="Id"/>);
  /// FK-констрейнт создан raw SQL в миграции — не смоделирован в EF (alternate key требует NOT NULL).
  /// </summary>
  public int? OrgCompId { get; set; }
}
