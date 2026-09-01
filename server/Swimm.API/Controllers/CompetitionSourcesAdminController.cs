using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// «Источники» на странице соревнования (/Admin/Competitions): из каких compID федерации
/// состоит его стартовый протокол.
///
/// Импорт привязывает свой compID сам, но у окружных чемпионатов источников несколько на
/// один наш старт («8-11 חורף 2026» — север, центр ×2, юг), и собрать их может только
/// человек. Подробности модели — <see cref="Swimm.Domain.Entities.CompetitionSource"/>.
/// </summary>
[ApiController]
[Route("api/admin/competition-sources")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class CompetitionSourcesAdminController : ControllerBase
{
    private readonly ICompetitionSourceAdminService _sources;
    private readonly IAdminAuditService _audit;

    public CompetitionSourcesAdminController(ICompetitionSourceAdminService sources, IAdminAuditService audit)
    {
        _sources = sources;
        _audit = audit;
    }

    /// <summary>Привязки соревнования + кандидаты из «Входящих» по датам его дней.</summary>
    [HttpGet("{competitionId:int}")]
    public async Task<IActionResult> Get(int competitionId)
    {
        try
        {
            return Ok(await _sources.GetAsync(competitionId, HttpContext.RequestAborted));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPost("link")]
    public async Task<IActionResult> Link([FromBody] SourceLinkRequest request)
    {
        try
        {
            var view = await _sources.LinkAsync(
                request.CompetitionId, request.OrgCompId, HttpContext.RequestAborted);

            await _audit.LogAsync("competition.source-link", "Competition",
                request.CompetitionId.ToString(),
                $"Источник стартового протокола: соревнование {request.CompetitionId} ← compID {request.OrgCompId}",
                new { request.CompetitionId, request.OrgCompId }, HttpContext.RequestAborted);

            return Ok(view);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("unlink")]
    public async Task<IActionResult> Unlink([FromBody] SourceLinkRequest request)
    {
        try
        {
            var view = await _sources.UnlinkAsync(
                request.CompetitionId, request.OrgCompId, HttpContext.RequestAborted);

            await _audit.LogAsync("competition.source-unlink", "Competition",
                request.CompetitionId.ToString(),
                $"Источник отвязан: соревнование {request.CompetitionId} ✕ compID {request.OrgCompId}",
                new { request.CompetitionId, request.OrgCompId }, HttpContext.RequestAborted);

            return Ok(view);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    public record SourceLinkRequest(int CompetitionId, int OrgCompId);
}
