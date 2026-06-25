using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Справочник стилей плавания (обычно 5–7 записей).
/// </summary>
[Index(nameof(Name), IsUnique = true)]
public class Style
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>freestyle, backstroke, breaststroke, butterfly, individual_medley, etc.</summary>
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;
}
