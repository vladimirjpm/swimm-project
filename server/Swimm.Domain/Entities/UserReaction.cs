using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Реакция пользователя: лайк на медиа (❤ на видео/фото) или поздравление на заплыв (🎉).
/// Одна реакция на пользователя+цель — уникальность через partial unique индексы в миграции
/// (по образцу Sys_UserFavorites). Kind определяет цель: like → MediaId, congrats → ResultId.
/// </summary>
public class UserReaction
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = null!;

    /// <summary>like / congrats</summary>
    [Required, MaxLength(20)]
    public string Kind { get; set; } = string.Empty;

    public int? MediaId { get; set; }

    [ForeignKey(nameof(MediaId))]
    public UserMedia? Media { get; set; }

    public long? ResultId { get; set; }

    [ForeignKey(nameof(ResultId))]
    public ResultRecord? ResultRecord { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
