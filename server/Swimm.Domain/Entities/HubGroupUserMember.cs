using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>Статусы участия аккаунта в группе.</summary>
public static class HubGroupUserMemberStatus
{
    /// <summary>Активный участник (мгновенная самозапись или добавление владельцем/админом).</summary>
    public const string Active = "active";
    /// <summary>Заявка на вступление ожидает решения (дверь под будущую модерацию).</summary>
    public const string Pending = "pending";
}

/// <summary>
/// Участник группы — аккаунт сайта (в отличие от <see cref="HubGroupMember"/>, который ссылается
/// на пловца из справочника <see cref="Swimmer"/>). Личные данные → Sys_-таблица, БЕЗ grant
/// swimm_ro: приватный список, публичная страница группы его не видит (читается роль swimm_ro).
/// Заводится самозаписью пользователя (в публичную группу) или владельцем/админом по email.
/// НЕ даёт прав на группу — это «состав»/watchlist из аккаунтов, не владение и не админство.
/// </summary>
public class HubGroupUserMember
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

    /// <summary>Кто добавил. null = самозапись пользователя.</summary>
    public int? AddedByUserId { get; set; }

    [ForeignKey(nameof(AddedByUserId))]
    public AppUser? AddedBy { get; set; }

    /// <summary>active | pending (см. <see cref="HubGroupUserMemberStatus"/>).</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = HubGroupUserMemberStatus.Active;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Ярлык «за какого пловца этот аккаунт» (напр. родитель) — чисто отображение, прав не
    /// меняет. null = не привязан. SetNull при удалении пловца — не терять членство.
    /// </summary>
    public int? SwimmerId { get; set; }

    [ForeignKey(nameof(SwimmerId))]
    public Swimmer? Swimmer { get; set; }

    /// <summary>Произвольная подпись к ярлыку («родитель», «бабушка», …), опционально.</summary>
    [MaxLength(100)]
    public string? Note { get; set; }
}
