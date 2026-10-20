namespace Swimm.Application.Abstractions;

/// <summary>Итог отправки одного push (docs/plans/web-push-reminders-plan.md §6).</summary>
public enum PushDeliveryOutcome
{
    /// <summary>Push-служба приняла сообщение.</summary>
    Delivered,

    /// <summary>404/410 — подписка умерла (разрешение отозвано, браузер переустановлен): строку удалить.</summary>
    Gone,

    /// <summary>Другая ошибка (сеть, 5xx, 429) — подписка жива, попробуем в следующий раз.</summary>
    Failed,
}

/// <summary>Подписка браузера — то, что вернул <c>PushManager.subscribe()</c>.</summary>
public sealed record PushTarget(string Endpoint, string P256dh, string Auth);

/// <summary>
/// Что показать: заголовок, текст, куда открыть по тапу, <paramref name="Tag"/> — повтор с тем же
/// тегом заменяет уведомление.
/// </summary>
public sealed record PushPayload(string Title, string Body, string Url, string Tag);

/// <summary>
/// Отправка одного Web Push (шифрование + подпись VAPID). Реализация — Infrastructure
/// (<c>WebPushSender</c>, Lib.Net.Http.WebPush). Ключи — конфиг <c>WebPush:*</c>; без них
/// <see cref="IsConfigured"/> = false, и всё, что шлёт push, молчит.
/// </summary>
public interface IPushSender
{
    bool IsConfigured { get; }

    /// <summary>Публичный ключ VAPID (base64url) для <c>PushManager.subscribe</c>; null — не настроено.</summary>
    string? PublicKey { get; }

    Task<PushDeliveryOutcome> SendAsync(PushTarget target, PushPayload payload, CancellationToken ct = default);
}
