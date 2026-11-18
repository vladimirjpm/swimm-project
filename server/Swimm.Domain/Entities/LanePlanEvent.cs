using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>Что случилось с опубликованным планом (<see cref="LanePlanEvent.Kind"/>).</summary>
public static class LanePlanEventKind
{
    /// <summary>План опубликован (<see cref="LanePlanEvent.OnLanes"/> — сколько людей на дорожках).</summary>
    public const string Published = "published";

    /// <summary>Ответ «Going» поставил человека в план (на дорожку его уровня или в Unassigned).</summary>
    public const string Going = "going";

    /// <summary>Ответ «Can't come» снял человека с плана («Not today»).</summary>
    public const string CantCome = "cant_come";

    /// <summary>Тренер переставил человека руками (дорожка, Unassigned, «Not today»).</summary>
    public const string Moved = "moved";

    public static readonly string[] All = [Published, Going, CantCome, Moved];
}

/// <summary>
/// Событие ОПУБЛИКОВАННОГО плана дорожек — лента «Since publishing» на рабочем столе тренера
/// (docs/plans/trainings-coach-flow-plan.md, CF8): что изменилось с момента, когда участникам
/// сообщили. Черновик событий не пишет; Unpublish ленту стирает, новая публикация начинает её
/// заново. ПРИВАТНЫЕ данные — <c>Sys_LanePlanEvents</c>, БЕЗ grant <c>swimm_ro</c>; видит только
/// управляющий.
///
/// Место в плане кодируется числом: null — не в плане («Not today»), 0 — Unassigned, N — дорожка N.
/// </summary>
public class LanePlanEvent
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public int PlanId { get; set; }

    public LanePlan? Plan { get; set; }

    /// <summary>Когда (UTC).</summary>
    public DateTime At { get; set; }

    /// <summary><see cref="LanePlanEventKind"/>.</summary>
    [Required, MaxLength(16)]
    public string Kind { get; set; } = "";

    /// <summary>Чей перенос; у <see cref="LanePlanEventKind.Published"/> — null.</summary>
    public int? SwimmerId { get; set; }

    [ForeignKey(nameof(SwimmerId))]
    public Swimmer? Swimmer { get; set; }

    /// <summary>Откуда: null — не был в плане, 0 — Unassigned, N — дорожка.</summary>
    public int? FromLane { get; set; }

    /// <summary>Куда: null — снят с плана, 0 — Unassigned, N — дорожка.</summary>
    public int? ToLane { get; set; }

    /// <summary>У <see cref="LanePlanEventKind.Published"/>: сколько людей стояло на дорожках.</summary>
    public int? OnLanes { get; set; }

    /// <summary>Кто сделал: тренер (перенос, публикация, ответ за человека) или участник (свой ответ).</summary>
    public int? ActorUserId { get; set; }

    [ForeignKey(nameof(ActorUserId))]
    public AppUser? Actor { get; set; }

    /// <summary>Ответ поставил тренер за человека (а не сам человек).</summary>
    public bool ByCoach { get; set; }
}
