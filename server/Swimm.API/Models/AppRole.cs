using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.API.Models;

/// <summary>
/// ???? ???????????? (Admin, User ? ?. ?.).
/// </summary>
public class AppRole
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>???????? ???? (??????????)</summary>
    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;
}
