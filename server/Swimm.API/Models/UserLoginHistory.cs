using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.API.Models;

/// <summary>
/// ??????? ?????? ????????????.
/// </summary>
public class UserLoginHistory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = null!;

    /// <summary>?????????, ????? ??????? ???????? ????</summary>
    [Required, MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>IP-????? (???? ????????)</summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    public DateTime LoginAt { get; set; } = DateTime.UtcNow;
}
