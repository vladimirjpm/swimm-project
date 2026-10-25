using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;

namespace Swimm.API.Controllers;

/// <summary>
/// Реакции: ❤ на медиа (видимое пользователю) и 🎉 на заплыв (публичный).
/// Идемпотентные тогглы — ответ всегда итоговое состояние {count, mine}
/// для оптимистичного UI. Только залогиненные, антифорджери, rate-limit.
/// </summary>
[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("reactions")]
public class ReactionsController : ControllerBase
{
    private readonly IReactionRepository _reactions;

    public ReactionsController(IReactionRepository reactions)
    {
        _reactions = reactions;
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    [HttpPost("/api/media/{id:int}/like")]
    public Task<IActionResult> Like(int id) => ToggleLike(id, on: true);

    [HttpDelete("/api/media/{id:int}/like")]
    public Task<IActionResult> Unlike(int id) => ToggleLike(id, on: false);

    [HttpPost("/api/results/{id:long}/cheer")]
    public Task<IActionResult> Cheer(long id) => ToggleCheer(id, on: true);

    [HttpDelete("/api/results/{id:long}/cheer")]
    public Task<IActionResult> Uncheer(long id) => ToggleCheer(id, on: false);

    /// <summary>
    /// 🎉 пачки заплывов для витрины: <c>?ids=1,2,3</c>. Читать может и гость (число публично,
    /// имён в ответе нет) — <c>mine</c> у него всегда false. Ответ личный (<c>mine</c>), поэтому
    /// без кэша. Лимит СВОЙ, а не «reactions» класса: мягкий, по IP (<see cref="CheerRules"/>) —
    /// страница протокола спрашивает пачками и в лимит кликов упиралась бы.
    /// </summary>
    [HttpGet("/api/results/cheers")]
    [AllowAnonymous]
    [EnableRateLimiting(CheerRules.ReadRateLimitPolicy)]
    public async Task<IActionResult> GetCheers([FromQuery] string? ids)
    {
        var parsed = (ids ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s, out var v) ? v : 0)
            .Where(v => v > 0)
            .Distinct()
            .ToList();
        if (parsed.Count == 0) return Ok(Array.Empty<object>());
        if (parsed.Count > CheerRules.MaxIdsPerRead) return BadRequest(new { error = $"At most {CheerRules.MaxIdsPerRead} ids" });

        Response.Headers.CacheControl = "private, no-store";
        return Ok(await _reactions.GetCheersAsync(parsed, CurrentUserId()));
    }

    private async Task<IActionResult> ToggleLike(int mediaId, bool on)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var state = await _reactions.SetLikeAsync(userId.Value, mediaId, on, User.IsInRole("Admin"));
        // 404 и для невидимого медиа — не раскрываем существование чужих приватных записей.
        return state == null ? NotFound(new { error = "Media not found" }) : Ok(state);
    }

    private async Task<IActionResult> ToggleCheer(long resultId, bool on)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var state = await _reactions.SetCheerAsync(userId.Value, resultId, on);
        return state == null ? NotFound(new { error = "Result not found" }) : Ok(state);
    }
}
