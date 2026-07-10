using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Со-тренер группы (SwimHub) — право ПОЛЬЗОВАТЕЛЯ управлять конкретной группой
/// (правка данных/состава), но не владеть ею и не назначать других со-тренеров.
/// Не путать с site-ролью Coach (<see cref="AppRole"/>) и с бейджем
/// <see cref="HubGroupMember.Role"/>="coach" на пловце в составе группы.
/// </summary>
public class HubGroupManager
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int HubGroupId { get; set; }

    [ForeignKey(nameof(HubGroupId))]
    public HubGroup? HubGroup { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser? User { get; set; }

    /// <summary>Кто назначил со-тренера (владелец или админ).</summary>
    public int GrantedByUserId { get; set; }

    [ForeignKey(nameof(GrantedByUserId))]
    public AppUser? GrantedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
