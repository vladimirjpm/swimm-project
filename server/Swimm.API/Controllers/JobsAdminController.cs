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

    /// <summary>
    /// Мониторинг /Admin/Jobs одним JSON — контракт в docs/admin-pages/jobs.md («Контракт
    /// мониторинга»). Опрашивается раз в 30 с: подробности прогонов сюда не входят.
    /// </summary>
    [HttpGet("monitor")]
    public async Task<IActionResult> Monitor(CancellationToken ct) => Ok(await jobs.GetMonitorAsync(ct));

    /// <summary>Один прогон с подробностями (<c>Details</c>) — окно «Что изменилось» и сырой JSON.</summary>
    [HttpGet("runs/{id:long}")]
    public async Task<IActionResult> Run(long id, CancellationToken ct) =>
        await jobs.GetRunDetailsAsync(id, ct) is { } run ? Ok(run) : NotFound();
}
