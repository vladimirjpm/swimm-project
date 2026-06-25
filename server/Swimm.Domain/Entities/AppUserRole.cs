using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Связь пользователя и роли (many-to-many).
/// </summary>
public class AppUserRole
{
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = null!;

    public int RoleId { get; set; }

    [ForeignKey(nameof(RoleId))]
    public AppRole Role { get; set; } = null!;
}
