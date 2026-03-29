using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.API.Models;

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
}
