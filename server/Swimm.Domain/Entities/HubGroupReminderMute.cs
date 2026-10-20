using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>«Не напоминать про эту группу» (§5 плана; переключатель «Remind me» — П3).</summary>
public class HubGroupReminderMute
{
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser? User { get; set; }

    public int HubGroupId { get; set; }

    [ForeignKey(nameof(HubGroupId))]
    public HubGroup? HubGroup { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
