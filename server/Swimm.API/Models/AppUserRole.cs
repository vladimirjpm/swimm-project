using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.API.Models;

/// <summary>
/// ????? ???????????? ? ????? (many-to-many).
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
