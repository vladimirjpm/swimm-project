using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Отмена конкретного занятия группы — «в этот вторник тренировки нет» (Р9,
/// docs/plans/trainings-tab-redesign-plan.md). Занятие — по расписанию или разовое
/// (<see cref="TrainingSession.AsksRsvp"/>) — адресуется парой «дата + начало», как и ответы
/// (<see cref="HubGroupTrainingRsvp"/>): ключ <c>yyyy-MM-dd-HHmm</c>. Строка есть — занятие
/// отменено; вернули — строку удаляют, ответы на занятие при отмене НЕ трогаются.
///
/// ПРИВАТНЫЕ данные — <c>Sys_HubGroupTrainingCancellations</c>, БЕЗ grant <c>swimm_ro</c>.
/// UNIQUE (<see cref="HubGroupId"/>, <see cref="Date"/>, <see cref="Start"/>).
/// </summary>
public class HubGroupTrainingCancellation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int HubGroupId { get; set; }

    [ForeignKey(nameof(HubGroupId))]
    public HubGroup? HubGroup { get; set; }

    /// <summary>Дата занятия (календарная, по Израилю).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Начало занятия «HH:mm» — по стенным часам, как в расписании.</summary>
    [Required, MaxLength(5)]
    public string Start { get; set; } = "";

    /// <summary>Причина для людей («Pool closed»); null — не указана.</summary>
    [MaxLength(200)]
    public string? Reason { get; set; }

    /// <summary>Кто отменил; null — аккаунт удалён (SetNull).</summary>
    public int? CreatedByUserId { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public AppUser? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
