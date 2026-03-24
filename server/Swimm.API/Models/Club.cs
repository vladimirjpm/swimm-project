using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.API.Models;

/// <summary>
/// ?????????? ??????.
/// </summary>
[Index(nameof(Name))]
public class Club
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>???????? ????? (?????/????????)</summary>
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>???????? ????? (????.)</summary>
    [MaxLength(200)]
    public string NameEn { get; set; } = string.Empty;
}
