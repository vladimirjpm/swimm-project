using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.API.Controllers;

/// <summary>
/// Жалоба «Report» на чужое медиа из лайтбокса (Р62, docs/data-integrity.md). Только залогиненные,
/// антифорджери, свой rate limit (свободный текст «Other»). Разбирает админ сайта на
/// /Admin/MediaReports. Ответ не говорит, спрятано ли медиа и сколько на него жалоб.
/// </summary>
[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting(MediaReportRules.RateLimitPolicy)]
public class MediaReportsController : ControllerBase
{
    private readonly IMediaReportService _reports;

    public MediaReportsController(IMediaReportService reports) => _reports = reports;

    [HttpPost("/api/media/{id:int}/report")]
    public async Task<IActionResult> Report(int id, [FromBody] SubmitMediaReportRequest request)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();

        var result = await _reports.ReportAsync(userId, id, request, User.IsInRole("Admin"));
        return result.Outcome switch
        {
            MediaReportOutcome.Accepted => Ok(new MediaReportResponseDto { AlreadyReported = result.AlreadyReported }),
            // 404 и для невидимого — не раскрываем существование чужих приватных записей.
            MediaReportOutcome.NotFound => NotFound(new { error = result.Error }),
            _ => BadRequest(new { error = result.Error }),
        };
    }
}
