using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Откуда пловец в составе группы (docs/plans/hubgroup-club-subscription-plan.md §2–§3).
/// </summary>
public static class HubGroupMemberSource
{
    /// <summary>Добавлен руками владельцем/админом группы. Подписку на клуб не слушает никогда.</summary>
    public const string Manual = "manual";

    /// <summary>Пришёл из подписки группы на клуб; пересборка состава его добавляет и убирает.</summary>
    public const string Club = "club";
}

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

    /// <summary>
    /// <see cref="HubGroupMemberSource.Manual"/> | <see cref="HubGroupMemberSource.Club"/>.
    /// Ручной побеждает клубного: пловец, добавленный руками, остаётся manual, даже если он
    /// есть в клубе подписки, и отписка его не трогает.
    /// </summary>
    [Required, MaxLength(10)]
    public string Source { get; set; } = HubGroupMemberSource.Manual;

    /// <summary>
    /// Владелец скрыл клубного пловца: строка остаётся, чтобы пересборка его не вернула, но
    /// НИ ОДИН читатель состава (страница, счётчики, ростер, медиа) его не видит. Только у
    /// клубных строк (CK_HubGroupMembers_ExcludedOnlyClub) — ручного убирают удалением.
    /// </summary>
    public bool IsExcluded { get; set; }
}
