using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Персональный план на соревнование — за кем следит пользователь в табе Start list
/// (docs/plans/start-list-ticket-plan.md, шаг Т3): несколько пловцов + клубы целиком,
/// плюс две галочки. Один план на пару «пользователь + соревнование».
///
/// ⚠ Таблица <c>Sys_</c> и БЕЗ гранта swimm_ro — в отличие от самих заявок. Заявки публичны
/// (это открытый протокол федерации), а план говорит, где будет ИМЕННО ЭТОТ ребёнок и что
/// его родитель собирается прийти; §8 плана стартового протокола.
///
/// Идентичность соревнования — <see cref="OrgCompId"/>, как у заявки: у предстоящего старта
/// строки в <c>Competitions</c> ещё нет (инвариант И7 + §3.1 плана).
/// </summary>
public class UserStartListPlan
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = null!;

    /// <summary>compID соревнования на isr.org.il. FK нет — см. комментарий к классу.</summary>
    public int OrgCompId { get; set; }

    /// <summary>
    /// Id выбранных пловцов через запятую («10,42,77»); пусто — никого.
    ///
    /// Строка, а не таблица-связка и не <c>integer[]</c>: список читается и пишется только
    /// целиком (сохранили состав — прочитали состав), запросов «кто следит за пловцом X» в
    /// плане нет, а FK тут был бы вреден — пловца могли слить дедупом, и план от этого
    /// падать не должен. Тот же приём, что <c>DiscoveredCompetition.Languages</c>.
    /// Неизвестные id молча отбрасываются при показе — пловец мог сняться.
    /// </summary>
    [MaxLength(1000)]
    public string SwimmerIds { get; set; } = string.Empty;

    /// <summary>Id клубов, за которыми следят целиком, — тем же форматом.</summary>
    [MaxLength(1000)]
    public string ClubIds { get; set; } = string.Empty;

    /// <summary>«I'm coming» — одно из трёх условий блока ARRIVE BY (§2 хендоффа).</summary>
    public bool ImComing { get; set; }

    /// <summary>
    /// «Notify me when it's out» на экране «протокол не опубликован». Рассылки пока НЕТ
    /// (решение Влада 29.08.2026: делаем кнопку, механизм уведомлений — отдельная работа);
    /// флаг копится, чтобы, когда механизм появится, было кого уведомить.
    /// </summary>
    public bool NotifyMe { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
