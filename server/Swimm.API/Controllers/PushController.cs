using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.API.Controllers;

/// <summary>
/// Web Push: подписки браузеров, «Send test» и настройки напоминаний
/// (docs/plans/web-push-reminders-plan.md §6). Всё — про свой аккаунт (userId из куки), данные
/// личные — <c>no-store</c>. Мутации — antiforgery и rate limit <see cref="PushRules.RateLimitPolicy"/>.
/// Публичный ключ VAPID — без входа: он и так уходит в каждый браузер.
/// </summary>
[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting(PushRules.RateLimitPolicy)]
public class PushController(IPushSubscriptionService push, IPushSender sender) : ControllerBase
{
    /// <summary>Заголовок, в котором клиент называет свой endpoint (пометить «этот браузер»): не в URL — адрес подписки секретный.</summary>
    public const string EndpointHeader = "X-Push-Endpoint";

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Публичный ключ VAPID для <c>PushManager.subscribe</c>; <c>public_key: null</c> — сервер push не шлёт.</summary>
    [HttpGet("/api/push/vapid-public-key")]
    [AllowAnonymous]
    public IActionResult PublicKey() => Ok(new { public_key = sender.PublicKey });

    [HttpPost("/api/push/subscriptions")]
    public async Task<IActionResult> Subscribe([FromBody] PushSubscriptionInputDto input, CancellationToken ct)
    {
        if (CurrentUserId() is not int userId) return Unauthorized();
        if (!sender.IsConfigured) return StatusCode(503, new { error = "Notifications are not available yet." });

        var error = await push.SubscribeAsync(userId, input, Request.Headers.UserAgent.ToString(), ct);
        return error == null ? NoContent() : BadRequest(new { error });
    }

    [HttpDelete("/api/push/subscriptions")]
    public async Task<IActionResult> Unsubscribe([FromBody] PushUnsubscribeInputDto input, CancellationToken ct)
    {
        if (CurrentUserId() is not int userId) return Unauthorized();
        if (string.IsNullOrWhiteSpace(input.Endpoint)) return BadRequest(new { error = "Endpoint is required." });
        await push.UnsubscribeAsync(userId, input.Endpoint.Trim(), ct);
        return NoContent();
    }

    /// <summary>Убрать устройство из списка (потерянный телефон).</summary>
    [HttpDelete("/api/push/devices/{deviceId:int}")]
    public async Task<IActionResult> RemoveDevice(int deviceId, CancellationToken ct)
    {
        if (CurrentUserId() is not int userId) return Unauthorized();
        return await push.RemoveDeviceAsync(userId, deviceId, ct) ? NoContent() : NotFound(new { error = "Device not found." });
    }

    /// <summary>«Send test»: push себе на все свои устройства.</summary>
    [HttpPost("/api/push/test")]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        if (CurrentUserId() is not int userId) return Unauthorized();
        if (!sender.IsConfigured) return StatusCode(503, new { error = "Notifications are not available yet." });

        var result = await push.SendTestAsync(userId, ct);
        Response.Headers.CacheControl = "private, no-store";
        return Ok(result);
    }

    [HttpGet("/api/me/notification-settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        if (CurrentUserId() is not int userId) return Unauthorized();
        var current = Request.Headers[EndpointHeader].ToString();
        var dto = await push.GetSettingsAsync(userId, string.IsNullOrWhiteSpace(current) ? null : current.Trim(), ct);
        Response.Headers.CacheControl = "private, no-store";
        return Ok(dto);
    }

    [HttpPut("/api/me/notification-settings")]
    public async Task<IActionResult> SaveSettings([FromBody] NotificationSettingsInputDto input, CancellationToken ct)
    {
        if (CurrentUserId() is not int userId) return Unauthorized();
        var error = await push.SaveSettingsAsync(userId, input, ct);
        if (error != null) return BadRequest(new { error });
        return await GetSettings(ct);
    }

    /// <summary>«Remind me» на группу (П3): <c>{ remind: false }</c> — не напоминать про неё.</summary>
    [HttpPut("/api/me/notification-settings/groups/{hubGroupId:int}")]
    public async Task<IActionResult> SetGroup(int hubGroupId, [FromBody] GroupReminderInputDto input, CancellationToken ct)
    {
        if (CurrentUserId() is not int userId) return Unauthorized();
        await push.SetGroupMutedAsync(userId, hubGroupId, input.Remind, ct);
        return NoContent();
    }
}
