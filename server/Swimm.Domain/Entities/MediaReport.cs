using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Жалоба «Report» на чужое медиа (Р62, docs/data-integrity.md). Sys_-таблица, БЕЗ grant swimm_ro:
/// кто пожаловался — видит только админ сайта; тренер группы видит число жалоб и причины, без
/// имён. Одна жалоба на медиа от аккаунта (уникальный индекс). Открытых жалоб набралось до порога
/// (настройка <c>MediaReportHideThreshold</c>) — медиа прячется везде
/// (<see cref="UserMedia.ModerationState"/>) до решения админа сайта.
/// Значения полей — <c>MediaReportRules</c>.
/// </summary>
public class MediaReport
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int UserMediaId { get; set; }

    [ForeignKey(nameof(UserMediaId))]
    public UserMedia Media { get; set; } = null!;

    /// <summary>Кто пожаловался. Удалили аккаунт — его жалобы уходят каскадом.</summary>
    public int ReporterUserId { get; set; }

    [ForeignKey(nameof(ReporterUserId))]
    public AppUser Reporter { get; set; } = null!;

    /// <summary>wrong_swimmer / inappropriate / spam / privacy / other.</summary>
    [Required, MaxLength(30)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Свободный текст — обязателен у «other», у остальных необязателен. Показывается только админу сайта.</summary>
    [MaxLength(500)]
    public string? Comment { get; set; }

    /// <summary>open / kept (админ оставил медиа) / removed (админ снял медиа).</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "open";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Кто решил (админ сайта). Без FK — как актор в AdminAudit: решение переживает удаление аккаунта.</summary>
    public int? DecidedByUserId { get; set; }

    public DateTime? DecidedAt { get; set; }
}
