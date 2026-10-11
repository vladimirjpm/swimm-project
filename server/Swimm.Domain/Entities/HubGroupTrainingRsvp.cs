using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>Ответ на тренировку: иду / не уверен / не приду.</summary>
public static class TrainingRsvpAnswer
{
    public const string Yes = "yes";
    public const string Maybe = "maybe";
    public const string No = "no";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Yes, Maybe, No };
}

/// <summary>Быстрая заметка к ответу «иду» / «не уверен» (хендофф group-club-changes §6).</summary>
public static class TrainingRsvpNote
{
    public const string Late = "late";
    public const string FirstHour = "first-hour";
    public const string LeavingEarly = "leaving-early";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Late, FirstHour, LeavingEarly };
}

/// <summary>
/// Ответ на занятие группы (docs/plans/entity-hero-roles-plan.md, Ш2).
/// ПРИВАТНЫЕ данные — <c>Sys_HubGroupTrainingRsvps</c>, БЕЗ grant <c>swimm_ro</c>.
///
/// Субъект — ровно одно из двух. Обычно отвечает ПОЛЬЗОВАТЕЛЬ, а не пловец (решение Влада
/// 28.09.2026); тренер может поставить ответ за участника (<see cref="SetByUserId"/>). У пловца
/// группы (Р71) аккаунта нет — за него отвечает только тренер, строка на <see cref="SwimmerId"/>
/// (Р73). Занятие не строка в базе — расписание регулярное
/// (<see cref="Swimm.Domain.GroupTrainingSchedule"/>), поэтому оно адресуется парой
/// «дата + начало слота» по стенным часам бассейна. Сменили расписание — ответы на
/// исчезнувшие слоты просто перестают показываться.
/// </summary>
public class HubGroupTrainingRsvp
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int HubGroupId { get; set; }

    [ForeignKey(nameof(HubGroupId))]
    public HubGroup? HubGroup { get; set; }

    /// <summary>Дата занятия (календарная, по Израилю).</summary>
    public DateOnly SessionDate { get; set; }

    /// <summary>Начало слота «HH:mm» — как в расписании.</summary>
    [Required, MaxLength(5)]
    public string SessionStart { get; set; } = "";

    /// <summary>Чей ответ — аккаунт участника; null — ответ за пловца группы. Cascade: удалили аккаунт — ответ уходит с ним.</summary>
    public int? UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser? User { get; set; }

    /// <summary>
    /// Пловец группы (<c>Swimmer.PrivateHubGroupId</c>), за которого ответил тренер; null — ответ
    /// аккаунта. Cascade: пловца удалили насовсем — ответы уходят с ним.
    /// </summary>
    public int? SwimmerId { get; set; }

    [ForeignKey(nameof(SwimmerId))]
    public Swimmer? Swimmer { get; set; }

    /// <summary><see cref="TrainingRsvpAnswer"/>.</summary>
    [Required, MaxLength(8)]
    public string Answer { get; set; } = TrainingRsvpAnswer.Yes;

    /// <summary><see cref="TrainingRsvpNote"/>; null — без заметки.</summary>
    [MaxLength(20)]
    public string? Note { get; set; }

    /// <summary>
    /// Кто поставил ответ, если НЕ сам участник (тренер по просьбе в WhatsApp). null — сам.
    /// SetNull при удалении аккаунта тренера — ответ остаётся.
    /// </summary>
    public int? SetByUserId { get; set; }

    [ForeignKey(nameof(SetByUserId))]
    public AppUser? SetBy { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
