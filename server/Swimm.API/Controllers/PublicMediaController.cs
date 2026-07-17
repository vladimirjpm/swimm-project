using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// Публичная точка видимости медиа заплывов (этап 4 media-visibility-model): иконки видео
/// в таблице результатов. Аноним видит только approved public публикации; залогиненный —
/// плюс своё медиа и members-публикации своих групп. Per-viewer → без общего кэша.
/// </summary>
[ApiController]
[Route("api/media")]
public class PublicMediaController : ControllerBase
{
    private readonly IUserMediaPublicationService _publications;

    public PublicMediaController(IUserMediaPublicationService publications)
    {
        _publications = publications;
    }

    /// <summary>Видимое зрителю медиа заплывов соревнования, события или group-режима (?group=slug).</summary>
    [HttpGet("results")]
    public async Task<IActionResult> GetForResults(
        [FromQuery] int? competitionId, [FromQuery] int? eventId, [FromQuery] string? group)
    {
        if (competitionId == null && eventId == null && string.IsNullOrWhiteSpace(group))
            return BadRequest(new { error = "competitionId, eventId or group is required" });

        if (group is { Length: > 120 })
            return BadRequest(new { error = "group slug too long" });

        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int? userId = int.TryParse(raw, out var id) ? id : null;

        return Ok(await _publications.GetVisibleForResultsAsync(competitionId, eventId, group, userId));
    }
}
