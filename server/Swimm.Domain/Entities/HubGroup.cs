using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Группа (SwimHub) — неформальное тренировочное объединение пловцов из разных клубов,
/// созданное пользователем. НЕ <see cref="Club"/> (официальный справочник федерации)
/// и НЕ <see cref="Relay"/> (эстафетная команда одного заплыва).
/// </summary>
[Index(nameof(Slug), IsUnique = true)]
public class HubGroup
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Название группы (иврит/кириллица)</summary>
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Название группы (англ.)</summary>
    [MaxLength(200)]
    public string? NameEn { get; set; }

    /// <summary>Для будущего URL /group/{slug}</summary>
    [Required, MaxLength(120)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    /// <summary>Медиа = только ссылки (решение по проекту)</summary>
    [MaxLength(1000)]
    public string? IconUrl { get; set; }

    /// <summary>Обложка страницы группы</summary>
    [MaxLength(1000)]
    public string? CoverImageUrl { get; set; }

    /// <summary>JSON-массив [{"kind":"whatsapp","url":"…"}]; kind: whatsapp/telegram/instagram/site</summary>
    [MaxLength(2000)]
    public string? Links { get; set; }

    /// <summary>Город/бассейн, свободный текст</summary>
    [MaxLength(200)]
    public string? Location { get; set; }

    /// <summary>Если группа фактически при клубе (опционально)</summary>
    public int? ClubId { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    /// <summary>Кто создал/управляет группой</summary>
    public int OwnerUserId { get; set; }

    [ForeignKey(nameof(OwnerUserId))]
    public AppUser? Owner { get; set; }

    /// <summary>Пока значение диктует глобальная настройка HubGroupVisibility; работает только при perGroup.</summary>
    public bool IsPublic { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<HubGroupMember> Members { get; set; } = new List<HubGroupMember>();

    /// <summary>Со-тренеры группы (право правки, не владение).</summary>
    public ICollection<HubGroupManager> Managers { get; set; } = new List<HubGroupManager>();
}
