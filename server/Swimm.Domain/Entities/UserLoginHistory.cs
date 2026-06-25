using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// История входов пользователя.
/// </summary>
public class UserLoginHistory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = null!;

    /// <summary>Провайдер, через который выполнен вход</summary>
    [Required, MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>IP-адрес (если доступен)</summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    public DateTime LoginAt { get; set; } = DateTime.UtcNow;
}
