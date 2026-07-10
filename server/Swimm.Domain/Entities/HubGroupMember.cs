using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Участник группы (<see cref="HubGroup"/>) — привязка пловца из справочника <see cref="Swimmer"/>.
/// </summary>
[Index(nameof(HubGroupId), nameof(SwimmerId), IsUnique = true)]
public class HubGroupMember
{
    /// <summary>Роли участника группы.</summary>
    public static readonly IReadOnlySet<string> Roles = new HashSet<string> { "member", "captain", "coach" };

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int HubGroupId { get; set; }

    [ForeignKey(nameof(HubGroupId))]
    public HubGroup? HubGroup { get; set; }

    public int SwimmerId { get; set; }

    [ForeignKey(nameof(SwimmerId))]
    public Swimmer? Swimmer { get; set; }

    /// <summary>member | captain | coach.</summary>
    [Required, MaxLength(20)]
    public string Role { get; set; } = "member";

    public int SortOrder { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
