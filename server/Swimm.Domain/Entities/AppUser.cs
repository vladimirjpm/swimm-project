using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Пользователь приложения.
/// </summary>
public class AppUser
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>E-mail (уникальный идентификатор)</summary>
    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Отображаемое имя</summary>
    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>URL аватара (из Google/Facebook)</summary>
    [MaxLength(1000)]
    public string? AvatarUrl { get; set; }

    /// <summary>Ссылка на спортсмена (опционально)</summary>
    public int? SwimmerId { get; set; }

    [ForeignKey(nameof(SwimmerId))]
    public Swimmer? Swimmer { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // --- Навигация ---
    public ICollection<AppUserRole> UserRoles { get; set; } = new List<AppUserRole>();
    public ICollection<UserExternalLogin> ExternalLogins { get; set; } = new List<UserExternalLogin>();
}
