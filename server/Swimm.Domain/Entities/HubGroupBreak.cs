using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// «On break» — человек в группе временно (или совсем) не ходит: травма, отпуск, армия, бросил
/// (docs/plans/entity-hero-roles-plan.md §5, Ш3.1). Флаг ОДИН на человека, но людей в группе
/// двух видов, поэтому субъект — ровно одно из двух: аккаунт-участник (<see cref="UserId"/>) или
/// пловец состава без аккаунта (<see cref="SwimmerId"/>). Связку «аккаунт ↔ его пловец» решает
/// чтение, а не запись.
///
/// ПРИВАТНЫЕ данные — <c>Sys_HubGroupBreaks</c>, БЕЗ grant <c>swimm_ro</c>: «перестал плавать»
/// наружу не выходит. Строки — история: перерыв закрывается (<see cref="EndedAt"/>), а не
/// удаляется, — тренеру нужно «вернулся после 5 месяцев». Открытая строка у субъекта одна
/// (частичный UNIQUE по <c>EndedAt IS NULL</c>).
/// </summary>
public class HubGroupBreak
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int HubGroupId { get; set; }

    [ForeignKey(nameof(HubGroupId))]
    public HubGroup? HubGroup { get; set; }

    /// <summary>Аккаунт-участник; ровно одно из <see cref="UserId"/> / <see cref="SwimmerId"/>.</summary>
    public int? UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser? User { get; set; }

    /// <summary>Пловец состава (как правило — без аккаунта: большинство Дельфина из сидера).</summary>
    public int? SwimmerId { get; set; }

    [ForeignKey(nameof(SwimmerId))]
    public Swimmer? Swimmer { get; set; }

    /// <summary>С какого момента на перерыве.</summary>
    public DateTime Since { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Последний день перерыва (календарный, по Израилю) включительно; после него — снова в
    /// строю без чьих-либо действий. null — бессрочно (так ставит тренер «бросил»).
    /// </summary>
    public DateOnly? Until { get; set; }

    /// <summary>Когда перерыв сняли руками или ответом «Going». null — не снимали.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>Сняли ответом «Going» (сам вернулся) — для пометки тренеру «back after N months».</summary>
    public bool EndedByRsvp { get; set; }

    /// <summary>Кто поставил; SetNull при удалении аккаунта.</summary>
    public int? SetByUserId { get; set; }

    [ForeignKey(nameof(SetByUserId))]
    public AppUser? SetBy { get; set; }
}
