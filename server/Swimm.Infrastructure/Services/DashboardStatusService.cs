using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Сводка «Статус данных» для дашборда /Admin (docs/plans/admin-dashboard-status-cards-plan.md):
/// дубли пловцов/клубов (на лету, через существующие dedup-сервисы), привязка Loglig ID
/// и разбор Discovery. Один запрос вместо четырёх с клиента.
/// </summary>
public class DashboardStatusService(
    SwimmDbContext db,
    ISwimmerDedupService swimmerDedup,
    IClubDedupService clubDedup) : IDashboardStatusService
{
    public async Task<DashboardStatusSummary> GetStatusAsync(CancellationToken ct = default)
    {
        var swimmerReport = await swimmerDedup.FindCandidatesAsync(ct);
        var clubReport = await clubDedup.FindCandidatesAsync(ct);

        var swimmers = new DashboardSwimmerStatus(
            swimmerReport.Orphans.Count,
            swimmerReport.Candidates.Count(c => c.Sure),
            swimmerReport.Candidates.Count(c => !c.Sure));

        var clubs = new DashboardClubStatus(
            clubReport.Candidates.Count(c => c.Sure),
            clubReport.Candidates.Count(c => !c.Sure));

        var logligCounts = await db.Swimmers.AsNoTracking()
            .GroupBy(s => s.LogligIdStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var loglig = new DashboardLogligStatus(
            Verified: logligCounts.FirstOrDefault(g => g.Status == "Verified")?.Count ?? 0,
            Suggested: logligCounts.FirstOrDefault(g => g.Status == "Suggested")?.Count ?? 0,
            Rejected: logligCounts.FirstOrDefault(g => g.Status == "Rejected")?.Count ?? 0,
            Unlinked: logligCounts.FirstOrDefault(g => g.Status == null)?.Count ?? 0);

        // Совпадает с /Admin/Discovery: строка считается импортированной, если она матчится с
        // Competitions по имени+дате (см. DiscoveryCompetitionMatcher) ИЛИ помечена вручную через
        // SetStatusAsync — ничего в пайплайне импорта Status=Imported само не выставляет.
        var discoveryRows = await db.DiscoveredCompetitions.AsNoTracking().ToListAsync(ct);
        var matches = await new DiscoveryCompetitionMatcher(db).MatchAsync(discoveryRows, ct);

        var imported = 0;
        var newCount = 0;
        var other = 0;
        foreach (var row in discoveryRows)
        {
            var isMatched = matches.GetValueOrDefault(row.Id) is not null;
            if (isMatched || row.Status == DiscoveredCompetitionStatus.Imported)
                imported++;
            else if (row.Status == DiscoveredCompetitionStatus.Ignored)
                other++;
            else
                newCount++;
        }

        var discovery = new DashboardDiscoveryStatus(Imported: imported, New: newCount, Other: other);

        var media = new DashboardMediaStatus(
            Total: await db.UserMedia.CountAsync(ct),
            Broken: await db.UserMedia.CountAsync(m => m.LinkOk == false, ct),
            Unchecked: await db.UserMedia.CountAsync(m => m.LinkCheckedAt == null, ct));

        return new DashboardStatusSummary(swimmers, clubs, loglig, discovery, media);
    }
}
