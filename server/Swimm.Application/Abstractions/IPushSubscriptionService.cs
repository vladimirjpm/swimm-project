using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Push-подписки и настройки напоминаний пользователя (docs/plans/web-push-reminders-plan.md §6).
/// Все методы — про СВОЙ аккаунт: <paramref name="userId"/> берёт контроллер из куки.
/// </summary>
public interface IPushSubscriptionService
{
    /// <summary>
    /// Upsert по endpoint: новый браузер — новая строка, тот же — обновить ключи и владельца
    /// (вошёл другой человек). Сверх <c>PushRules.MaxDevicesPerUser</c> вытесняется самое старое.
    /// null — успех, иначе текст для 400.
    /// </summary>
    Task<string?> SubscribeAsync(int userId, PushSubscriptionInputDto input, string? userAgent, CancellationToken ct = default);

    /// <summary>Удалить свою подписку по адресу; чужую (или несуществующую) — молча ничего.</summary>
    Task UnsubscribeAsync(int userId, string endpoint, CancellationToken ct = default);

    /// <summary>Удалить своё устройство из списка по id.</summary>
    Task<bool> RemoveDeviceAsync(int userId, int deviceId, CancellationToken ct = default);

    /// <summary>Настройки + устройства; <paramref name="currentEndpoint"/> — пометить «этот браузер».</summary>
    Task<NotificationSettingsDto> GetSettingsAsync(int userId, string? currentEndpoint, CancellationToken ct = default);

    /// <summary>Сохранить настройки; null — успех, иначе текст для 400.</summary>
    Task<string?> SaveSettingsAsync(int userId, NotificationSettingsInputDto input, CancellationToken ct = default);

    /// <summary>«Remind me» на группу (П3): <paramref name="remind"/> = false — не напоминать про неё.</summary>
    Task SetGroupMutedAsync(int userId, int hubGroupId, bool remind, CancellationToken ct = default);

    /// <summary>«Send test»: push себе на все свои устройства; умершие удаляются.</summary>
    Task<PushTestResultDto> SendTestAsync(int userId, CancellationToken ct = default);
}
