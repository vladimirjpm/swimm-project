using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Подписка браузера на push — <c>POST /api/push/subscriptions</c> (docs/plans/web-push-reminders-plan.md
/// §6). Форма — как у <c>PushSubscription.toJSON()</c>: <c>{ endpoint, keys: { p256dh, auth } }</c>,
/// клиент шлёт её как есть.
/// </summary>
public sealed class PushSubscriptionInputDto
{
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }

    [JsonPropertyName("keys")]
    public PushSubscriptionKeysDto? Keys { get; set; }
}

public sealed class PushSubscriptionKeysDto
{
    [JsonPropertyName("p256dh")]
    public string? P256dh { get; set; }

    [JsonPropertyName("auth")]
    public string? Auth { get; set; }
}

/// <summary>Отписать браузер — <c>DELETE /api/push/subscriptions</c>; удаляется только своя строка.</summary>
public sealed class PushUnsubscribeInputDto
{
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }
}

/// <summary>Устройство в списке «Your devices». Адрес подписки наружу не отдаём — только признак «это я».</summary>
public sealed class PushDeviceDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("last_success_at")]
    public DateTime? LastSuccessAt { get; set; }

    [JsonPropertyName("last_failure_at")]
    public DateTime? LastFailureAt { get; set; }

    /// <summary>Это браузер, из которого спрашивают (клиент передал свой endpoint).</summary>
    [JsonPropertyName("is_current")]
    public bool IsCurrent { get; set; }
}

/// <summary>
/// Настройки напоминаний — <c>GET/PUT /api/me/notification-settings</c>. Нет строки в базе —
/// умолчания (В1, В4, В6): включены, за 2 часа, без письма.
/// </summary>
public sealed class NotificationSettingsDto
{
    [JsonPropertyName("reminders_enabled")]
    public bool RemindersEnabled { get; set; } = true;

    /// <summary>1h | 2h | evening (<c>ReminderLead</c>).</summary>
    [JsonPropertyName("lead")]
    public string Lead { get; set; } = "2h";

    [JsonPropertyName("email_fallback")]
    public bool EmailFallback { get; set; }

    /// <summary>Группы, о которых не напоминать («Remind me», П3). PUT настроек их не меняет — для этого PUT …/groups/{id}.</summary>
    [JsonPropertyName("muted_group_ids")]
    public List<int> MutedGroupIds { get; set; } = [];

    /// <summary>Сервер умеет слать push (ключи VAPID заданы). false — UI прячет кнопку.</summary>
    [JsonPropertyName("push_configured")]
    public bool PushConfigured { get; set; }

    [JsonPropertyName("devices")]
    public List<PushDeviceDto> Devices { get; set; } = [];
}

/// <summary>Правка настроек; null — поле не трогать.</summary>
public sealed class NotificationSettingsInputDto
{
    [JsonPropertyName("reminders_enabled")]
    public bool? RemindersEnabled { get; set; }

    [JsonPropertyName("lead")]
    public string? Lead { get; set; }

    [JsonPropertyName("email_fallback")]
    public bool? EmailFallback { get; set; }
}

/// <summary>«Remind me» на группу — <c>PUT /api/me/notification-settings/groups/{id}</c>.</summary>
public sealed class GroupReminderInputDto
{
    [JsonPropertyName("remind")]
    public bool Remind { get; set; } = true;
}

/// <summary>Итог «Send test»: на сколько устройств ушло, сколько умерло и удалено.</summary>
public sealed class PushTestResultDto
{
    [JsonPropertyName("devices")]
    public int Devices { get; set; }

    [JsonPropertyName("delivered")]
    public int Delivered { get; set; }

    [JsonPropertyName("removed")]
    public int Removed { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }
}

/// <summary>Итог прогона напоминаний — строка в истории /Admin/Jobs.</summary>
public sealed record TrainingReminderReport(int Groups, int Planned, int Sent, int Skipped, int Failed, int RemovedDevices);
