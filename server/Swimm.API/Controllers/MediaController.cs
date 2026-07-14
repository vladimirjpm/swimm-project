using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Validation;

namespace Swimm.API.Controllers;

/// <summary>
/// 2A: личное owner-only медиа пловца (ссылки youtube/vimeo/other). Публичного слоя
/// нет — см. docs/tasks/user-media-2a-sonnet.md (2B добавит /api/media + visibility=public).
/// </summary>
[ApiController]
[Route("api/me/media")]
[Authorize]
[AutoValidateAntiforgeryToken]
public class MediaController : ControllerBase
{
    private readonly IUserMediaRepository _media;

    public MediaController(IUserMediaRepository media)
    {
        _media = media;
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    [HttpGet]
    public async Task<IActionResult> GetMedia([FromQuery] int? swimmerId)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        return Ok(await _media.GetForUserAsync(userId.Value, swimmerId));
    }

    [HttpPost]
    public async Task<IActionResult> AddMedia([FromBody] AddUserMediaRequest request)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        if (request.SwimmerId <= 0)
            return BadRequest(new { error = "swimmer_id is required" });

        if (!MediaUrlValidator.TryValidate(request.MediaType, request.SourceType, request.Url, out var error))
            return BadRequest(new { error });

        var media = await _media.AddAsync(userId.Value, request);
        if (media == null)
            return BadRequest(new { error = "Swimmer not found" });

        return CreatedAtAction(nameof(GetMedia), media);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> RemoveMedia(int id)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var ok = await _media.RemoveAsync(userId.Value, id);
        return ok ? NoContent() : NotFound(new { error = "Media not found" });
    }
}
