using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>Видимость медиа группы (вне тренировок).</summary>
public static class HubGroupMediaVisibility
{
    /// <summary>Витрина группы — публичная галерея на странице groups.html (как исторически).</summary>
    public const string Public = "public";
    /// <summary>Только активным user-членам группы — тренерские разборы (2B′).
    /// Требует официальной группы; допускает якорь пловец/заплыв.</summary>
    public const string Members = "members";
}

/// <summary>
/// Медиа группы (SwimHub) — только ссылки, по образцу <see cref="UserMedia"/>.
/// <see cref="TrainingId"/> null = публичная галерея группы (страница groups.html);
/// задан = медиа конкретной приватной тренировки — видно той же аудитории, что и сама тренировка.
/// 2B′ (docs/favorites-media-phase2-design.md): + <see cref="Visibility"/> members-слой
/// (тренерские разборы, только официальные группы) с якорем пловец/заплыв.
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

    /// <summary>public | members (см. <see cref="HubGroupMediaVisibility"/>). Для медиа
    /// тренировок (TrainingId != null) не используется — там аудитория тренировки.</summary>
    [Required, MaxLength(20)]
    public string Visibility { get; set; } = HubGroupMediaVisibility.Public;

    /// <summary>Якорь «разбор пловца». Только при Visibility=members — публично вешать медиа
    /// на персону нельзя. При заданном <see cref="ResultId"/> денормализуется из заплыва.</summary>
    public int? SwimmerId { get; set; }

    [ForeignKey(nameof(SwimmerId))]
    public Swimmer? Swimmer { get; set; }

    /// <summary>Якорь «разбор заплыва» (только при Visibility=members).</summary>
    public long? ResultId { get; set; }

    [ForeignKey(nameof(ResultId))]
    public ResultRecord? Result { get; set; }

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
