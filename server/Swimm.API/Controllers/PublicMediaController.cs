using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// Публичная точка видимости медиа заплывов (этап 4 media-visibility-model): иконки видео
/// в таблице результатов. Аноним видит только approved public публикации; залогиненный —
/// плюс своё медиа и members-публикации групп, где он участник или управляющий (правило
/// аудитории — MediaPublicationAudience). Per-viewer → без общего кэша.
/// </summary>
[ApiController]
[Route("api/media")]
public class PublicMediaController : ControllerBase
{
    private readonly IUserMediaPublicationService _publications;
    private readonly IUserMediaRepository _media;
    private readonly IHubGroupPublicRepository _groups;

    public PublicMediaController(
        IUserMediaPublicationService publications, IUserMediaRepository media, IHubGroupPublicRepository groups)
    {
        _publications = publications;
        _media = media;
        _groups = groups;
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
        var isSiteAdmin = User.IsInRole("Admin");

        // Group-режим приватной группы: медиа её ростера — данные группы, не-участнику пусто (§6-6).
        if (competitionId == null && eventId == null && group != null)
        {
            var access = await _groups.GetAccessAsync(group, userId, isSiteAdmin);
            if (access is { CanView: false }) return Ok(Array.Empty<object>());
        }

        return Ok(await _publications.GetVisibleForResultsAsync(
            competitionId, eventId, group, userId, isSiteAdmin));
    }

    /// <summary>
    /// Видимое зрителю медиа пловца — галерея на странице пловца (swimmer.html?swimmer=id).
    /// Аноним — только approved public; залогиненный — плюс своё и members своих групп.
    /// </summary>
    [HttpGet("/api/swimmers/{id:int}/media")]
    public async Task<IActionResult> GetForSwimmer(int id)
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int? userId = int.TryParse(raw, out var uid) ? uid : null;
        return Ok(await _publications.GetVisibleForSwimmerAsync(id, userId, User.IsInRole("Admin")));
    }
}
