using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>Статус плана дорожек: черновик видят только управляющие, опубликованный — участники.</summary>
public static class LanePlanStatus
{
    public const string Draft = "draft";
    public const string Published = "published";
}

/// <summary>
/// План дорожек группы на дату (docs/plans/lane-plans-plan.md, L2): сколько дорожек, какой
/// уровень и задание на каждой, кто где плывёт. ПРИВАТНЫЕ данные — <c>Sys_LanePlans</c>, БЕЗ
/// grant <c>swimm_ro</c>. План — СНИМОК: смена уровней или состава группы его не переписывает.
/// Один план на дату (UNIQUE HubGroupId+Date) — снять, когда появится время начала.
/// </summary>
public class LanePlan
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int HubGroupId { get; set; }

    [ForeignKey(nameof(HubGroupId))]
    public HubGroup? HubGroup { get; set; }

    /// <summary>Дата тренировки (календарная, по Израилю).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Сколько дорожек свободно (1..12). Строки дорожек 1..LaneCount есть всегда.</summary>
    public int LaneCount { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary><see cref="LanePlanStatus.Draft"/> | <see cref="LanePlanStatus.Published"/>.</summary>
    [Required, MaxLength(10)]
    public string Status { get; set; } = LanePlanStatus.Draft;

    /// <summary>Кто завёл план. SetNull при удалении аккаунта — план группы остаётся.</summary>
    public int? CreatedByUserId { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public AppUser? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<LanePlanLane> Lanes { get; set; } = new List<LanePlanLane>();

    public ICollection<LanePlanSwimmer> Swimmers { get; set; } = new List<LanePlanSwimmer>();
}

/// <summary>Дорожка плана: уровень (для «Distribute» и подписи) и задание текстом.</summary>
public class LanePlanLane
{
    public int PlanId { get; set; }

    public LanePlan? Plan { get; set; }

    /// <summary>Номер дорожки, 1..LaneCount плана.</summary>
    public int LaneNo { get; set; }

    /// <summary>Уровень этой же группы (проверяет сервис); удалили уровень — SET NULL, подпись пропадает.</summary>
    public int? LevelId { get; set; }

    public HubGroupLevel? Level { get; set; }

    /// <summary>Задание на дорожку — пока обычный текст (структурные сеты — позже).</summary>
    [MaxLength(4000)]
    public string? Workout { get; set; }
}

/// <summary>
/// Пловец в плане. <see cref="LaneNo"/> задан — на дорожке; null — «Unassigned» (пришёл, не
/// разложен). Строки нет вовсе — «Not today» (снят тренером). Флагов для корзин не нужно.
/// </summary>
public class LanePlanSwimmer
{
    public int PlanId { get; set; }

    public LanePlan? Plan { get; set; }

    public int SwimmerId { get; set; }

    public Swimmer? Swimmer { get; set; }

    public int? LaneNo { get; set; }

    /// <summary>Порядок внутри дорожки (кто ведёт) — как прислал редактор.</summary>
    public int OrderNo { get; set; }
}
