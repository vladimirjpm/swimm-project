using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Push-подписка одного браузера (docs/plans/web-push-reminders-plan.md §5): адрес push-службы
/// (FCM у Chrome, Apple у Safari, Mozilla у Firefox) и ключи шифрования, которые выдал
/// <c>PushManager.subscribe()</c>. Привязана к аккаунту, а не к телефону: номер не собираем.
///
/// ⚠ ЛИЧНЫЕ данные и привязка к устройству — <c>Sys_UserPushSubscriptions</c>, БЕЗ grant
/// swimm_ro и НЕ в seed-tables: подписка из прод-базы, попавшая в локальную, разослала бы
/// тестовые push живым людям.
///
/// <see cref="Endpoint"/> уникален: один браузер — одна строка. Вошёл другой человек и включил
/// напоминания в том же браузере — строка переезжает к нему.
/// </summary>
public class UserPushSubscription
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public AppUser? User { get; set; }

    /// <summary>Адрес push-службы для этого браузера — по сути секрет: кто его знает, тот и шлёт.</summary>
    [Required, MaxLength(1000)]
    public string Endpoint { get; set; } = "";

    /// <summary>Публичный ключ браузера (P-256, base64url) — им шифруется сообщение.</summary>
    [Required, MaxLength(200)]
    public string P256dh { get; set; } = "";

    /// <summary>Общий секрет аутентификации (base64url).</summary>
    [Required, MaxLength(100)]
    public string Auth { get; set; } = "";

    /// <summary>Подпись устройства для списка «Your devices» — из User-Agent («iPhone · Safari»).</summary>
    [MaxLength(100)]
    public string? DeviceLabel { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Последняя доставка, которую push-служба приняла. iOS иногда «роняет» подписки — видно по давности.</summary>
    public DateTime? LastSuccessAt { get; set; }

    public DateTime? LastFailureAt { get; set; }

    /// <summary>Неудачи подряд (сбрасывается успехом). 404/410 строку удаляют сразу, без счётчика.</summary>
    public int FailureCount { get; set; }
}
