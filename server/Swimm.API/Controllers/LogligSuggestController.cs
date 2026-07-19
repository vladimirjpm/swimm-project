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
[Authorize]
[AutoValidateAntiforgeryToken]
public class LogligSuggestController(ILogligSuggestionService suggestions) : ControllerBase
{
    public sealed record SuggestRequest(int LogligId);

    [HttpPost("{id:int}/loglig-suggest")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Suggest(int id, [FromBody] SuggestRequest request, CancellationToken ct)
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId)) return Unauthorized();

        var result = await suggestions.SuggestAsync(id, request.LogligId, userId, ct);
        return result.Accepted ? Ok(result) : BadRequest(new { error = result.Error });
    }
}
