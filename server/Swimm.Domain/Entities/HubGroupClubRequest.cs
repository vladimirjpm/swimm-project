using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>Статусы заявки на официальный статус группы.</summary>
public static class HubGroupClubRequestStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

/// <summary>
/// Заявка владельца группы на привязку к клубу (официальный статус, фаза 8.7).
/// Личные данные заявителя → Sys_-таблица, БЕЗ grant swimm_ro.
/// Одобрение — единая транзакция: HubGroup.IsOfficial/ClubId + site-роль Coach
/// заявителю + bump SecurityStamp (см. HubGroupClubRequestAdminService).
/// </summary>
public class HubGroupClubRequest
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int HubGroupId { get; set; }

    [ForeignKey(nameof(HubGroupId))]
    public HubGroup? HubGroup { get; set; }

    /// <summary>Заявитель — владелец группы на момент подачи.</summary>
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser? User { get; set; }

    public int ClubId { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    [MaxLength(500)]
    public string? Message { get; set; }

    /// <summary>pending | approved | rejected</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = HubGroupClubRequestStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DecidedAt { get; set; }

    public int? DecidedByUserId { get; set; }

    [ForeignKey(nameof(DecidedByUserId))]
    public AppUser? DecidedByUser { get; set; }
}
