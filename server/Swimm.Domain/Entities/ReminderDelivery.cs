using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>Вид доставки (<see cref="ReminderDelivery.Kind"/>).</summary>
public static class ReminderKind
{
    /// <summary>Напоминание о ближайшем занятии.</summary>
    public const string Reminder = "reminder";

    /// <summary>«Training cancelled» при отмене тренером (П4).</summary>
    public const string Cancelled = "cancelled";
}

/// <summary>Итог доставки (<see cref="ReminderDelivery.Result"/>).</summary>
public static class ReminderResult
{
    /// <summary>Строка вставлена, отправка идёт (или процесс упал посреди неё).</summary>
    public const string Pending = "pending";

    /// <summary>Хотя бы одно устройство приняло.</summary>
    public const string Sent = "sent";

    /// <summary>Все подписки оказались мёртвыми (404/410) и удалены.</summary>
    public const string Gone = "gone";

    /// <summary>Ни одно устройство не приняло (сбой push-службы).</summary>
    public const string Failed = "failed";
}

/// <summary>
/// Журнал доставки напоминаний и ЗАЩИТА ОТ ДУБЛЯ (§4 п.2 плана): строка вставляется ДО отправки,
/// UNIQUE (UserId, HubGroupId, SessionDate, SessionStart, Kind). Не вставилась — уже отправлено
/// (другим процессом: ProdAdmin пишет в ту же базу; или до рестарта). Это страхует и от будущего
/// Scale out &gt; 1 — флаг процесса тут не защита.
///
/// Sys_, БЕЗ grant swimm_ro и НЕ в seed-tables. Занятие — пара «дата + начало», как у ответов
/// (<see cref="HubGroupTrainingRsvp"/>): строки занятия в базе нет.
/// </summary>
public class ReminderDelivery
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser? User { get; set; }

    public int HubGroupId { get; set; }

    [ForeignKey(nameof(HubGroupId))]
    public HubGroup? HubGroup { get; set; }

    public DateOnly SessionDate { get; set; }

    [Required, MaxLength(5)]
    public string SessionStart { get; set; } = "";

    /// <summary><see cref="ReminderKind"/>.</summary>
    [Required, MaxLength(20)]
    public string Kind { get; set; } = ReminderKind.Reminder;

    /// <summary>push | email (письмо — П4).</summary>
    [Required, MaxLength(10)]
    public string Channel { get; set; } = "push";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? SentAt { get; set; }

    /// <summary><see cref="ReminderResult"/>.</summary>
    [Required, MaxLength(20)]
    public string Result { get; set; } = ReminderResult.Pending;

    /// <summary>На сколько устройств ушло / сколько их было.</summary>
    public int Devices { get; set; }

    public int Delivered { get; set; }
}
