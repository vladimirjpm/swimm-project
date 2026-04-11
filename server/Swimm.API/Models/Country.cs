using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.API.Models;

/// <summary>
/// Справочник стран.
/// </summary>
[Index(nameof(CountryCode), IsUnique = true)]
public class Country
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Код страны (ISR, USA, GER и т.д.)</summary>
    [MaxLength(10)]
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>Название страны</summary>
    [MaxLength(100)]
    public string CountryName { get; set; } = string.Empty;
}
