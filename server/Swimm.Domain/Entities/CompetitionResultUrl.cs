using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// URL PDF-результатов соревнования у организатора (loglig.com и т.п.), по языкам.
/// Связь с <see cref="Competition"/> — через <see cref="OrgCompId"/> (не через Competition.Id).
/// FK-констрейнт создан raw SQL в миграции; в EF-модели навигация намеренно не объявлена
/// (alternate key на Competition.OrgCompId потребовал бы NOT NULL, а он должен быть nullable).
/// </summary>
public class CompetitionResultUrl
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Соответствует Competition.OrgCompId.</summary>
    public int OrgCompId { get; set; }

    /// <summary>Локаль экспорта, например "he-IL", "en-US".</summary>
    [MaxLength(20)]
    public string Culture { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Url { get; set; } = string.Empty;
}
