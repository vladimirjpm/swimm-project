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

    /// <summary>
    /// URL аватара (из Google/Facebook). Без ограничения длины (text): у аккаунтов Google под
    /// Family Link ссылка на фото длиннее 1000 символов, и прежний varchar(1000) ронял создание
    /// аккаунта целиком (16.10.2026, ошибка 500 при первом входе ребёнка).
    /// </summary>
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

    /// <summary>
    /// Персональный лимит групп, которыми пользователь может владеть, — исключение, которое
    /// выставляет админ в /Admin/Users. null — действует лимит по роли (настройки
    /// HubGroupMaxPerUser / HubGroupMaxPerCoach), 0 — создавать группы нельзя. Как это
    /// складывается с политикой и ролью — только в <c>HubGroupCreationRules</c>.
    /// </summary>
    public int? HubGroupLimit { get; set; }

    /// <summary>
    /// Когда человек отметил прежнюю галочку «мне 13+» (В4, 14–16.10.2026). С 16.10.2026 галочки нет —
    /// возраст при регистрации не спрашиваем (решение Влада после входа ребёнка через Family Link),
    /// новые записи — null. Колонка оставлена на случай родительского согласия в будущем.
    /// null — галочки не было (до 14.10 или после 16.10) или тестовый персонаж.
    /// </summary>
    public DateTime? AgeConfirmedAt { get; set; }

    // --- Опрос «Who are you?» на странице /setup-profile (docs/plans/profile-setup-plan.md) ---
    // Решение Влада 16.10.2026: ТОЛЬКО опрос, ни на что не влияет — ни на шаги и счётчик
    // «Set up profile», ни на права. Хранится, потому что может пригодиться в будущем.
    // ⚠ ProfileRoleCoach — ответ человека о себе, НЕ site-роль Coach (AppRole) и НЕ бейдж
    // HubGroupMember.Role = "coach". Права на нём не строить.

    /// <summary>Ответ опроса «I swim».</summary>
    public bool ProfileRoleSwimmer { get; set; }

    /// <summary>Ответ опроса «I'm a parent».</summary>
    public bool ProfileRoleParent { get; set; }

    /// <summary>Ответ опроса «I'm a coach» — не право, см. комментарий выше.</summary>
    public bool ProfileRoleCoach { get; set; }

    /// <summary>Ответ опроса «Just watching».</summary>
    public bool ProfileRoleWatcher { get; set; }

    /// <summary>
    /// Когда ответ опроса последний раз менялся. null — ни разу не отвечал (отличает «не отвечал»
    /// от «снял все галочки»).
    /// </summary>
    public DateTime? ProfileRolesAnsweredAt { get; set; }

    /// <summary>
    /// Когда нажал Dismiss на /setup-profile: пункт «Set up profile» в меню больше не показывается.
    /// null — пункт виден, пока не сделаны все шаги (<c>ProfileSetupRules</c>).
    /// </summary>
    public DateTime? ProfileSetupDismissedAt { get; set; }

    // --- Отказ от шага «Set up profile» (решение Влада 17.10.2026) ---
    // «Не про меня»: тренер отмечает только клуб, родитель — без «это я». Отказ засчитывает шаг
    // так же, как сделанный (ProfileSetupRules); снимается в любой момент. Прав не даёт.

    /// <summary>Отказ от шага «It's me» («Not a swimmer»).</summary>
    public bool ProfileSkipMe { get; set; }

    /// <summary>Отказ от шага «My club» («No club»).</summary>
    public bool ProfileSkipClub { get; set; }

    /// <summary>Отказ от шага «My group» («No group»).</summary>
    public bool ProfileSkipGroup { get; set; }

    // --- Навигация ---
    public ICollection<AppUserRole> UserRoles { get; set; } = new List<AppUserRole>();
    public ICollection<UserExternalLogin> ExternalLogins { get; set; } = new List<UserExternalLogin>();

    /// <summary>Локальные учётные данные (email+пароль). null — локального входа нет, только OAuth.</summary>
    public UserLocalCredential? LocalCredential { get; set; }
}
