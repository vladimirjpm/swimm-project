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
    private readonly IUserMediaRepository _media;

    public PublicMediaController(IUserMediaPublicationService publications, IUserMediaRepository media)
    {
        _publications = publications;
        _media = media;
    }

    /* Пикер привязки к заплыву (Add link, дизайн-бриф §6.3) — публичные данные результатов. */

    /// <summary>Соревнования пловца (для чипов пикера).</summary>
    [HttpGet("/api/swimmers/{id:int}/competitions-brief")]
    public async Task<IActionResult> GetSwimmerCompetitions(int id)
        => Ok(await _media.GetSwimmerCompetitionsBriefAsync(id));

    /// <summary>Заплывы пловца на соревновании (без эстафет).</summary>
    [HttpGet("/api/swimmers/{id:int}/results-brief")]
    public async Task<IActionResult> GetSwimmerResults(int id, [FromQuery] int competitionId)
    {
        if (competitionId <= 0) return BadRequest(new { error = "competitionId is required" });
        return Ok(await _media.GetSwimmerResultsBriefAsync(id, competitionId));
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
