using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Медиа группы (SwimHub) — только ссылки, по образцу <see cref="UserMedia"/>.
/// <see cref="TrainingId"/> null = публичная галерея группы (страница groups.html);
/// задан = медиа конкретной приватной тренировки — видно той же аудитории, что и сама тренировка.
/// </summary>
public class HubGroupMedia
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Обязателен — рычаг «стереть всё медиа группы» (cascade delete).</summary>
    public int HubGroupId { get; set; }

    [ForeignKey(nameof(HubGroupId))]
    public HubGroup? HubGroup { get; set; }

    /// <summary>null = публичная галерея группы; иначе — медиа этой тренировки.</summary>
    public int? TrainingId { get; set; }

    [ForeignKey(nameof(TrainingId))]
    public TrainingSession? Training { get; set; }

    /// <summary>image / video / album</summary>
    [Required, MaxLength(20)]
    public string MediaType { get; set; } = string.Empty;

    /// <summary>youtube / vimeo / album / other. Инвариант: MediaType=album ⇔ SourceType=album.</summary>
    [Required, MaxLength(20)]
    public string SourceType { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string Url { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Caption { get; set; }

    public int CreatedByUserId { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public AppUser? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
