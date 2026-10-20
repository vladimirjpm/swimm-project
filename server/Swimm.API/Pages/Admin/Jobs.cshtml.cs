using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.API.Services;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin;

/// <summary>
/// /Admin/Jobs — все фоновые службы процесса и что они делали (docs/admin-pages/jobs.md).
/// Карточки и сводку собирает <see cref="IBackgroundJobsStatus"/> (<see cref="BackgroundJobsStatusService"/>):
/// та же сводка кормит блок «Jobs» дашборда, подсчёт один.
/// </summary>
[Authorize(Roles = "Admin")]
public class JobsModel(IBackgroundJobsStatus jobs) : PageModel
{
    public const int HistoryRows = BackgroundJobsStatusService.HistoryRows;

    public BackgroundJobsReport Report { get; private set; } = null!;

    public IReadOnlyList<OverviewTile> Overview => Report.Summary.Tiles;

    public async Task OnGetAsync(CancellationToken ct) => Report = await jobs.GetReportAsync(ct);
}
