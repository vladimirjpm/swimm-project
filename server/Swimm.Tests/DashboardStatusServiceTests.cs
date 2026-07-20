using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сводка «Статус данных» для дашборда /Admin (docs/plans/admin-dashboard-status-cards-plan.md):
/// счётчики loglig по статусам, discovery по Status, сшивка со сводками дедупа.
/// Фейки дедуп-сервисов — простые классы-стабы (Moq в проекте нет).
/// </summary>
public class DashboardStatusServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private sealed class FakeSwimmerDedupService(SwimmerDedupReport report) : ISwimmerDedupService
    {
        public Task<SwimmerDedupReport> FindCandidatesAsync(CancellationToken ct = default) => Task.FromResult(report);
        public Task<SwimmerOrphanCleanupReport> DeleteOrphansAsync(IReadOnlyCollection<int>? ids, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeClubDedupService(ClubDedupReport report) : IClubDedupService
    {
        public Task<ClubDedupReport> FindCandidatesAsync(CancellationToken ct = default) => Task.FromResult(report);
    }

    private static SwimmerDedupReport SwimmerReport(int orphans, int sure, int unsure)
    {
        var report = new SwimmerDedupReport();
        for (var i = 0; i < orphans; i++)
            report.Orphans.Add(new SwimmerOrphan(i, "N", 2000, "isr", null));
        for (var i = 0; i < sure; i++)
            report.Candidates.Add(new SwimmerDedupCandidate(1, "A", null, 1, 2, "B", null, 1, 2000, "F", 0, Sure: true));
        for (var i = 0; i < unsure; i++)
            report.Candidates.Add(new SwimmerDedupCandidate(1, "A", null, 1, 2, "B", null, 1, 2000, "F", 2, Sure: false));
        return report;
    }

    private static ClubDedupReport ClubReport(int sure, int unsure)
    {
        var report = new ClubDedupReport();
        for (var i = 0; i < sure; i++)
            report.Candidates.Add(new ClubDedupCandidate(1, "A", null, 1, 2, "B", null, 1, "name", 0, Sure: true));
        for (var i = 0; i < unsure; i++)
            report.Candidates.Add(new ClubDedupCandidate(1, "A", null, 1, 2, "B", null, 1, "name", 0, Sure: false));
        return report;
    }

    private static Swimmer S(string? logligStatus, int? logligId = null) =>
        new() { LastName = "L", FirstName = "F", BirthYear = 2000, LogligIdStatus = logligStatus, LogligId = logligId };

    private static DiscoveredCompetition D(int orgCompId, string status, string name = "Comp") =>
        new() { OrgCompId = orgCompId, Name = name, DateStart = DateTime.UtcNow, DateEnd = DateTime.UtcNow, Status = status };

    [Fact]
    public async Task GetStatusAsync_CombinesDedupReportsWithSureUnsureCounts()
    {
        await using var db = CreateDb(nameof(GetStatusAsync_CombinesDedupReportsWithSureUnsureCounts));
        var service = new DashboardStatusService(
            db,
            new FakeSwimmerDedupService(SwimmerReport(orphans: 3, sure: 2, unsure: 5)),
            new FakeClubDedupService(ClubReport(sure: 1, unsure: 4)));

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(new DashboardSwimmerStatus(3, 2, 5), result.Swimmers);
        Assert.Equal(new DashboardClubStatus(1, 4), result.Clubs);
    }

    [Fact]
    public async Task GetStatusAsync_CountsLogligByStatus_NullIsUnlinked()
    {
        await using var db = CreateDb(nameof(GetStatusAsync_CountsLogligByStatus_NullIsUnlinked));
        db.Swimmers.AddRange(
            S("Verified", 1), S("Verified", 2), S("Verified", 3),
            S("Suggested", 4),
            S("Rejected", 5), S("Rejected", 6),
            S(null), S(null), S(null), S(null));
        await db.SaveChangesAsync();

        var service = new DashboardStatusService(
            db, new FakeSwimmerDedupService(SwimmerReport(0, 0, 0)), new FakeClubDedupService(ClubReport(0, 0)));

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(new DashboardLogligStatus(Verified: 3, Suggested: 1, Rejected: 2, Unlinked: 4), result.Loglig);
    }

    [Fact]
    public async Task GetStatusAsync_CountsDiscoveryByMatch_SameLogicAsDiscoveryList()
    {
        // Совпадает с /Admin/Discovery: матч по имени+дате считается импортом, даже если Status
        // в БД всё ещё "new" (пайплайн импорта его не выставляет). Ignored — отдельно в Other,
        // не смешивается с New. Ручная пометка Imported засчитывается, даже если матч не найден.
        await using var db = CreateDb(nameof(GetStatusAsync_CountsDiscoveryByMatch_SameLogicAsDiscoveryList));
        var matchedDate = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc);
        db.Competitions.Add(new Competition { Name = "ליגה מס 4", Date = "03/07/2026", PoolType = "25m" });
        db.DiscoveredCompetitions.AddRange(
            // матчится по имени+дате, но Status всё ещё "new" -> Imported
            new DiscoveredCompetition
            {
                OrgCompId = 1, Name = "ליגה מס 4", Status = DiscoveredCompetitionStatus.New,
                DateStart = matchedDate, DateEnd = matchedDate
            },
            // не матчится, Status "new" -> New
            D(2, DiscoveredCompetitionStatus.New, name: "Другое соревнование"),
            // Ignored, не матчится -> Other (не сливается с New)
            D(3, DiscoveredCompetitionStatus.Ignored, name: "Игнор"),
            // помечено вручную Imported, матча нет -> всё равно Imported
            D(4, DiscoveredCompetitionStatus.Imported, name: "Вручную помечено"));
        await db.SaveChangesAsync();

        var service = new DashboardStatusService(
            db, new FakeSwimmerDedupService(SwimmerReport(0, 0, 0)), new FakeClubDedupService(ClubReport(0, 0)));

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(new DashboardDiscoveryStatus(Imported: 2, New: 1, Other: 1), result.Discovery);
    }

    [Fact]
    public async Task GetStatusAsync_EmptyDb_ReturnsAllZeroes()
    {
        await using var db = CreateDb(nameof(GetStatusAsync_EmptyDb_ReturnsAllZeroes));
        var service = new DashboardStatusService(
            db, new FakeSwimmerDedupService(SwimmerReport(0, 0, 0)), new FakeClubDedupService(ClubReport(0, 0)));

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Equal(new DashboardLogligStatus(0, 0, 0, 0), result.Loglig);
        Assert.Equal(new DashboardDiscoveryStatus(0, 0, 0), result.Discovery);
    }
}
