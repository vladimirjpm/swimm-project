using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Справочник клубов.
/// </summary>
[Index(nameof(Name))]
public class Club
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Название клуба (иврит/кириллица)</summary>
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Название клуба (англ.)</summary>
    [MaxLength(200)]
    public string NameEn { get; set; } = string.Empty;

    /// <summary>
    /// Псевдоклуб: в графе клуба протокола стоит страна/сборная («USA», «Israel»,
    /// «M25», «Maccabiah MIX»), а не реальный клуб. Такие записи исключаются из
    /// club-summary и дедупа; страна уходит в <see cref="CountryId"/>.
    /// </summary>
    public bool IsPseudo { get; set; }

    /// <summary>Ссылка на страну (опционально)</summary>
    public int? CountryId { get; set; }

    [ForeignKey(nameof(CountryId))]
    public Country? Country { get; set; }
}
