using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Справочник спортсменов.
/// </summary>
[Index(nameof(LastName), nameof(FirstName))]
[Index(nameof(LastNameEn), nameof(FirstNameEn))]
public class Swimmer
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string LastNameEn { get; set; } = string.Empty;

    [MaxLength(100)]
    public string FirstNameEn { get; set; } = string.Empty;

    public int BirthYear { get; set; }

    /// <summary>Пол спортсмена (M / F)</summary>
    [MaxLength(10)]
    public string? Gender { get; set; }

    /// <summary>ID спортсмена в федерации</summary>
    [MaxLength(50)]
    public string? SwimmerOrgId { get; set; }

    /// <summary>URL аватара</summary>
    [MaxLength(1000)]
    public string? AvatarUrl { get; set; }

    /// <summary>Ссылка на клуб (опционально)</summary>
    public int? ClubId { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    /// <summary>Ссылка на страну (опционально)</summary>
    public int? CountryId { get; set; }

    [ForeignKey(nameof(CountryId))]
    public Country? Country { get; set; }
}
