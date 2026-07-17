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
    private readonly IUserMediaPublicationService _publications;
    private readonly IHubGroupPermissionService _groupPermissions;

    public MediaController(
        IUserMediaRepository media,
        IUserMediaPublicationService publications,
        IHubGroupPermissionService groupPermissions)
    {
        _media = media;
        _publications = publications;
        _groupPermissions = groupPermissions;
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

    /* — Публикации в группы (этап 2 media-visibility-model) — */

    /// <summary>Публикации всех моих медиа (статусы заявок для «Моих ссылок»).</summary>
    [HttpGet("publications")]
    public async Task<IActionResult> GetMyPublications()
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        return Ok(await _publications.GetForOwnerAsync(userId.Value));
    }

    /// <summary>Подать медиа в группу (level: members|public). Админ группы — сразу approved.</summary>
    [HttpPost("{id:int}/publications")]
    public async Task<IActionResult> SubmitPublication(int id, [FromBody] SubmitPublicationRequest request)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        // Привилегия группы (владелец/админ группы/site-админ) → авто-approve своей заявки.
        var perms = await _groupPermissions.GetPermissionsAsync(
            request.HubGroupId, userId.Value, User.IsInRole("Admin"));
        if (!perms.Exists) return BadRequest(new { error = "group not found" });

        var (success, error, publication) = await _publications.SubmitAsync(
            userId.Value, id, request, perms.CanEdit);
        if (!success) return BadRequest(new { error });

        return Ok(publication);
    }

    /// <summary>Отозвать публикацию своего медиа из группы (любой статус).</summary>
    [HttpDelete("{id:int}/publications/{hubGroupId:int}")]
    public async Task<IActionResult> WithdrawPublication(int id, int hubGroupId)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var ok = await _publications.WithdrawAsync(userId.Value, id, hubGroupId);
        return ok ? NoContent() : NotFound(new { error = "Publication not found" });
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
