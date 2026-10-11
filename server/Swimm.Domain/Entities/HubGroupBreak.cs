using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Вид флага «не в воде» (<see cref="HubGroupBreak.Kind"/>). Статус человека в группе один
/// (решение Влада 30.09.2026): Active (строки нет) / On break / Doesn't train / Inactive (строка
/// состава скрыта, <c>HubGroupMember.IsExcluded</c>).
/// </summary>
public static class HubGroupBreakKind
{
    /// <summary>Временно не ходит: травма, отпуск. Ответ «Going» снимает.</summary>
    public const string Break = "break";

    /// <summary>
    /// В группе, но с ней не тренируется (тренер-пловчиха, родитель-аккаунт): в Following, в
    /// результатах и рекордах группы есть, в «кто идёт», знаменателе и дорожках — нет. Бессрочно,
    /// ставит только тренер; «Going» в какой-то день статус НЕ снимает — человек посчитан на этот
    /// день, и всё.
    /// </summary>
    public const string NotTraining = "not_training";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Break, NotTraining };
}

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

    /// <summary>
    /// <see cref="HubGroupBreakKind"/>: перерыв или «не тренируется с группой». Открытая строка у
    /// субъекта одна — поэтому статус однозначный; смена вида переиспользует строку.
    /// </summary>
    [Required, MaxLength(16)]
    public string Kind { get; set; } = HubGroupBreakKind.Break;

    /// <summary>Кто поставил; SetNull при удалении аккаунта.</summary>
    public int? SetByUserId { get; set; }

    [ForeignKey(nameof(SetByUserId))]
    public AppUser? SetBy { get; set; }
}
