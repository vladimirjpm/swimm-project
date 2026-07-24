using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// Краудсорс-предложение Loglig ID (docs/loglig-id-plan.md, шаг 6): любой залогиненный может
/// предложить привязку пловцу без Verified-привязки. Пишется как Suggested, проверяется ночным
/// джобом. Анти-SSRF: принимается только числовой ID (извлечение из ссылки — на клиенте).
/// Rate-limit как у auth — защита от перебора/спама.
/// </summary>
[ApiController]
[Route("api/swimmers")]
[AutoValidateAntiforgeryToken]
public class LogligSuggestController(ILogligSuggestionService suggestions) : ControllerBase
{
    public sealed record SuggestRequest(int LogligId);

    [HttpPost("{id:int}/loglig-suggest")]
    [Authorize]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Suggest(int id, [FromBody] SuggestRequest request, CancellationToken ct)
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId)) return Unauthorized();

        var result = await suggestions.SuggestAsync(id, request.LogligId, userId, ct);
        return result.Accepted ? Ok(result) : BadRequest(new { error = result.Error });
    }

    /// <summary>Статус привязки для показа кнопки/бейджа. Публичный (без [Authorize]) —
    /// статус + logligId только при Verified (ссылка на публичную карточку), без аудита;
    /// не кэшируется (точечный дешёвый запрос).</summary>
    [HttpGet("{id:int}/loglig-status")]
    public async Task<IActionResult> Status(int id, CancellationToken ct)
    {
        var result = await suggestions.GetStatusAsync(id, ct);
        return Ok(new { status = result.Status, logligId = result.LogligId });
    }
}
