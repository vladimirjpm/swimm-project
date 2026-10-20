using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Ключи VAPID и контакт сервера (docs/plans/web-push-reminders-plan.md §6) — секция <c>WebPush</c>.
/// На проде — переменные App Service (<c>WebPush__PublicKey</c>, <c>WebPush__PrivateKey</c>,
/// <c>WebPush__Subject</c>), локально — gitignored <c>appsettings.Local.json</c>. Сгенерировать:
/// <c>dotnet run --project server/Swimm.API -- --generate-vapid-keys</c>.
/// ⚠ Смена ключей аннулирует ВСЕ подписки: сгенерировать один раз и больше не менять.
/// </summary>
public sealed class WebPushOptions
{
    public const string SectionName = "WebPush";

    public string PublicKey { get; init; } = "";
    public string PrivateKey { get; init; } = "";

    /// <summary><c>mailto:</c> или https-адрес сайта: push-служба пишет туда, если сервер шлёт лишнее.</summary>
    public string Subject { get; init; } = "mailto:noreply@swimhub.co.il";

    public bool IsConfigured => PublicKey.Length > 0 && PrivateKey.Length > 0;
}

/// <summary>
/// <see cref="IPushSender"/> на Lib.Net.Http.WebPush (выбор П0: живой репозиторий, шифрование
/// aes128gcm без BouncyCastle на net5+). Сообщение — JSON <c>{ title, body, url, tag }</c>, его
/// показывает <c>client/public/sw.js</c>. Без ключей — <see cref="IsConfigured"/> = false и
/// <see cref="SendAsync"/> отвечает Failed, ничего не отправляя.
/// </summary>
public sealed class WebPushSender : IPushSender
{
    /// <summary>
    /// Сколько push-служба держит сообщение для выключенного телефона. Напоминание после начала
    /// тренировки бессмысленно, а дольше двух часов до неё оно не живёт (кроме «вечером накануне»).
    /// </summary>
    private const int TimeToLiveSeconds = 6 * 60 * 60;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PushServiceClient? _client;
    private readonly ILogger<WebPushSender> _logger;
    private readonly WebPushOptions _options;

    public WebPushSender(HttpClient http, WebPushOptions options, ILogger<WebPushSender> logger)
    {
        _options = options;
        _logger = logger;
        if (!options.IsConfigured) return;

        _client = new PushServiceClient(http)
        {
            DefaultAuthentication = new VapidAuthentication(options.PublicKey, options.PrivateKey)
            {
                Subject = options.Subject,
            },
            DefaultTimeToLive = TimeToLiveSeconds,
        };
    }

    public bool IsConfigured => _client != null;

    public string? PublicKey => IsConfigured ? _options.PublicKey : null;

    public async Task<PushDeliveryOutcome> SendAsync(PushTarget target, PushPayload payload, CancellationToken ct = default)
    {
        if (_client == null) return PushDeliveryOutcome.Failed;

        var subscription = new PushSubscription { Endpoint = target.Endpoint };
        subscription.SetKey(PushEncryptionKeyName.P256DH, target.P256dh);
        subscription.SetKey(PushEncryptionKeyName.Auth, target.Auth);

        var message = new PushMessage(JsonSerializer.Serialize(new
        {
            title = payload.Title,
            body = payload.Body,
            url = payload.Url,
            tag = payload.Tag,
        }, Json))
        {
            // Напоминание — то, ради чего человек включил push: будить телефон можно.
            Urgency = PushMessageUrgency.High,
        };

        try
        {
            await _client.RequestPushMessageDeliveryAsync(subscription, message, ct);
            return PushDeliveryOutcome.Delivered;
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushDeliveryOutcome.Gone;
        }
        catch (PushServiceClientException ex)
        {
            // Адрес подписки в лог не пишем — по нему можно слать push человеку.
            _logger.LogWarning("Push не доставлен: {Host} ответил {Status} {Message}",
                HostOf(target.Endpoint), (int)ex.StatusCode, ex.Message);
            return PushDeliveryOutcome.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Push не доставлен: {Host} недоступен ({Error})", HostOf(target.Endpoint), ex.Message);
            return PushDeliveryOutcome.Failed;
        }
    }

    private static string HostOf(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host : "?";
}
