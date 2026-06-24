using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Внешняя OAuth-привязка (Google, Facebook и т. д.).
/// Один пользователь может иметь несколько внешних авторизаций.
/// </summary>
public class UserExternalLogin
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = null!;

    /// <summary>Название провайдера: Google, Facebook и т. д.</summary>
    [Required, MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>Уникальный ID пользователя у провайдера</summary>
    [Required, MaxLength(256)]
    public string ProviderKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
