using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.API.Services;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.API.Pages.Admin;

/// <summary>
/// /Admin/Jobs — все фоновые службы процесса и что они делали (docs/admin-pages/jobs.md).
/// Карточки и сводку собирает <see cref="IBackgroundJobsStatus"/> (<see cref="BackgroundJobsStatusService"/>):
/// та же сводка кормит блок «Jobs» дашборда, подсчёт один. Плюс попапы «что изменилось» у
/// прогонов discovery с изменениями (<see cref="RunChanges"/>).
/// </summary>
[Authorize(Roles = "Admin")]
public class JobsModel(IBackgroundJobsStatus jobs, ICompetitionDiscoveryService discovery) : PageModel
{
    public const int HistoryRows = BackgroundJobsStatusService.HistoryRows;

    /// <summary>Ключ ViewData, через который карточка (<c>_JobCard</c>) узнаёт кликабельные прогоны.</summary>
    public const string RunChangesKey = "RunChanges";

    public BackgroundJobsReport Report { get; private set; } = null!;

    public IReadOnlyList<OverviewTile> Overview => Report.Summary.Tiles;

    /// <summary>Попапы прогонов discovery по Id строки журнала: только у прогонов, где что-то изменилось.</summary>
    public IReadOnlyDictionary<long, DiscoveryRunChangesView> RunChanges { get; private set; } =
        new Dictionary<long, DiscoveryRunChangesView>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Report = await jobs.GetReportAsync(ct);
        RunChanges = await BuildRunChangesAsync(ct);
    }

    private async Task<IReadOnlyDictionary<long, DiscoveryRunChangesView>> BuildRunChangesAsync(CancellationToken ct)
    {
        var runs = Report.Schedules
            .Where(c => c.Key == JobNames.Discovery)
            .SelectMany(c => c.History)
            .Select(r => (r.Id, r.StartedAt, Details: JobRunDetails.ParseDiscovery(r.Details)))
            .Where(r => r.Details is not null)
            .ToList();
        if (runs.Count == 0) return new Dictionary<long, DiscoveryRunChangesView>();

        // Какой старт уже есть в базе — так же, как связывает /Admin/Competitions (штамп OrgCompId,
        // иначе матч имя+дата). Спрашиваем только когда есть что показывать.
        var competitionIdByOrgCompId = (await discovery.GetAllAsync(ct))
            .Where(d => d.MatchedCompetitionId is not null)
            .GroupBy(d => d.OrgCompId)
            .ToDictionary(g => g.Key, g => g.First().MatchedCompetitionId!.Value);

        return runs.ToDictionary(r => r.Id,
            r => DiscoveryRunChangesBuilder.Build(r.Id, r.StartedAt, r.Details!, competitionIdByOrgCompId));
    }
}
