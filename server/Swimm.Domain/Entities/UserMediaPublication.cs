using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>Целевой слой публикации личного медиа в группе.</summary>
public static class UserMediaPublicationLevel
{
    /// <summary>Видно активным user-членам группы (members-слой страницы группы).</summary>
    public const string Members = "members";
    /// <summary>Публичная галерея группы — видно любому посетителю сайта.</summary>
    public const string Public = "public";
}

/// <summary>Статус заявки на публикацию.</summary>
public static class UserMediaPublicationStatus
{
    /// <summary>Подано владельцем медиа, ждёт решения админа группы; никому не видно.</summary>
    public const string Pending = "pending";
    /// <summary>Одобрено — медиа видно аудитории уровня <see cref="UserMediaPublicationLevel"/>.</summary>
    public const string Approved = "approved";
    /// <summary>Отклонено/снято с публикации админом. Владелец может подать повторно
    /// (та же строка возвращается в pending — уникальный индекс media+group).</summary>
    public const string Rejected = "rejected";
}

/// <summary>
/// Публикация личного медиа (<see cref="UserMedia"/>) в группе (<see cref="HubGroup"/>) —
/// модель «одна запись — много публикаций» (память media-visibility-model): сама запись
/// остаётся приватной у владельца, а эта строка даёт её конкретной группе на уровне
/// members или public ПОСЛЕ одобрения админом группы. Удаление записи каскадно убирает
/// все её публикации; снятие с публикации запись не трогает.
/// </summary>
[Index(nameof(UserMediaId), nameof(HubGroupId), IsUnique = true)]
public class UserMediaPublication
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int UserMediaId { get; set; }

    [ForeignKey(nameof(UserMediaId))]
    public UserMedia? Media { get; set; }

    public int HubGroupId { get; set; }

    [ForeignKey(nameof(HubGroupId))]
    public HubGroup? HubGroup { get; set; }

    /// <summary>members | public (см. <see cref="UserMediaPublicationLevel"/>).</summary>
    [Required, MaxLength(20)]
    public string Level { get; set; } = UserMediaPublicationLevel.Members;

    /// <summary>pending | approved | rejected (см. <see cref="UserMediaPublicationStatus"/>).</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = UserMediaPublicationStatus.Pending;

    /// <summary>Кто решил (одобрил/отклонил/снял). null пока pending.</summary>
    public int? DecidedByUserId { get; set; }

    [ForeignKey(nameof(DecidedByUserId))]
    public AppUser? DecidedBy { get; set; }

    public DateTime? DecidedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
