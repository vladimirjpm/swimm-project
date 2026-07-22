using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swimm.Application.Abstractions;

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

    private async Task<IActionResult> ToggleLike(int mediaId, bool on)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var state = await _reactions.SetLikeAsync(userId.Value, mediaId, on);
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
