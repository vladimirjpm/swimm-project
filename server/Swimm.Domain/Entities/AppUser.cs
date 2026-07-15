using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Пользователь приложения.
/// </summary>
public class AppUser
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>E-mail (уникальный идентификатор)</summary>
    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Отображаемое имя</summary>
    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>URL аватара (из Google/Facebook)</summary>
    [MaxLength(1000)]
    public string? AvatarUrl { get; set; }

    /// <summary>Ссылка на спортсмена (опционально)</summary>
    public int? SwimmerId { get; set; }

    [ForeignKey(nameof(SwimmerId))]
    public Swimmer? Swimmer { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Меняется при отзыве доступа — служит для инвалидации активных сессий (cookie).
    /// Записывается в claims куки при логине; при каждой ре-валидации (OnValidatePrincipal)
    /// сверяется со значением в БД. Несовпадение → принудительный sign-out.
    /// Бампается при: деактивации, смене ролей, «выйти со всех устройств».
    /// </summary>
    [Required, MaxLength(64)]
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Последняя активность (обновляется в CookieSecurityStampValidator при успешной
    /// ре-валидации, т.е. не чаще раза в ~5 минут на сессию). «Онлайн сейчас» в админке =
    /// LastSeenAt свежее ~15 минут. null — не заходил после введения поля.
    /// </summary>
    public DateTime? LastSeenAt { get; set; }

    // --- Навигация ---
    public ICollection<AppUserRole> UserRoles { get; set; } = new List<AppUserRole>();
    public ICollection<UserExternalLogin> ExternalLogins { get; set; } = new List<UserExternalLogin>();

    /// <summary>Локальные учётные данные (email+пароль). null — локального входа нет, только OAuth.</summary>
    public UserLocalCredential? LocalCredential { get; set; }
}
