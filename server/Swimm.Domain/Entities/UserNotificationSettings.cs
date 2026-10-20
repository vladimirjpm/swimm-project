using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>За сколько до занятия напоминать (<see cref="UserNotificationSettings.LeadKind"/>).</summary>
public static class ReminderLead
{
    /// <summary>За час.</summary>
    public const string OneHour = "1h";

    /// <summary>За два часа — по умолчанию (решение В1).</summary>
    public const string TwoHours = "2h";

    /// <summary>Вечером накануне, в 20:00.</summary>
    public const string Evening = "evening";

    public const string Default = TwoHours;

    public static readonly IReadOnlySet<string> All = new HashSet<string> { OneHour, TwoHours, Evening };
}

/// <summary>
/// Настройки уведомлений пользователя (§5 плана). Строки нет — действуют умолчания:
/// напоминания включены (В6: разрешение push и есть согласие), за 2 часа (В1), без письма (В4).
/// Отдельная таблица, а не колонки <see cref="AppUser"/>: AppUser — корень кэша «только своей
/// строки», и клик по настройке не должен его трогать.
/// </summary>
public class UserNotificationSettings
{
    /// <summary>PK = FK на аккаунт: одна строка на человека.</summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser? User { get; set; }

    public bool RemindersEnabled { get; set; } = true;

    /// <summary><see cref="ReminderLead"/>: 1h | 2h | evening.</summary>
    [Required, MaxLength(20)]
    public string LeadKind { get; set; } = ReminderLead.Default;

    /// <summary>Письмо, если push не доставить (П4). Выключено по умолчанию (В4).</summary>
    public bool EmailFallback { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
