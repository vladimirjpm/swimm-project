using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// Сводка фоновых служб для блока «Jobs» дашборда (docs/admin-pages/index.md). Та же сводка, что
/// плитки вверху /Admin/Jobs, — считает <see cref="IBackgroundJobsStatus"/>. Живая, не в
/// двухминутной сводке дашборда: упавший прогон должен быть виден сразу.
/// </summary>
[ApiController]
[Route("api/admin/jobs")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class JobsAdminController(IBackgroundJobsStatus jobs) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct) => Ok(await jobs.GetSummaryAsync(ct));
}
