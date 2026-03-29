using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.API.Models;

/// <summary>
/// Роль пользователя (Admin, User и т. д.).
/// </summary>
public class AppRole
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Название роли (уникальное)</summary>
    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;
}
