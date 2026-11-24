using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Список /Admin/Competitions: значок находок Health у строки (по соревнованию и его строкам, без
/// находок по пловцу; у события — сумма по дням) и фильтр <c>?filter=problems</c> по всем сезонам.
/// </summary>
public class CompetitionProblemsListTests
{
    private static readonly DateOnly Today = new(2026, 11, 24);

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static DataCheckFinding Finding(string check, int sev, string type, int? id, string? resolution = null) => new()
    {
        CheckId = check, Severity = sev, EntityType = type, EntityId = id, Message = $"{check} #{id}", Resolution = resolution,
    };

    private static async Task<SwimmDbContext> SeedAsync(string name)
    {
        var db = CreateDb(name);
        db.Competitions.AddRange(
            new Competition { Id = 1, Name = "Чистый", Date = "10/10/2026", PoolType = "25m" },
            new Competition { Id = 2, Name = "С ошибкой", Date = "11/10/2026", PoolType = "25m" },
            // Событие из двух дней: находки по дням складываются.
            new Competition { Id = 3, Name = "Событие д1", Date = "05/03/2024", PoolType = "50m", EventId = 9, DayNumber = 1 },
            new Competition { Id = 4, Name = "Событие д2", Date = "06/03/2024", PoolType = "50m", EventId = 9, DayNumber = 2 },
            new Competition { Id = 5, Name = "Только пловец", Date = "12/10/2026", PoolType = "25m" });
        db.CompetitionEvents.Add(new CompetitionEvent { Id = 9, Name = "Событие" });
        db.Results.AddRange(
            new ResultRecord { Id = 100, CompetitionId = 3 },
            new ResultRecord { Id = 101, CompetitionId = 4 },
            new ResultRecord { Id = 102, CompetitionId = 4 },
            new ResultRecord { Id = 103, CompetitionId = 5 });
        db.DataCheckFindings.AddRange(
            Finding("results.merged-sessions", 2, "Competition", 2),
            Finding("results.no-gender", 1, "Result", 100),
            Finding("results.no-gender", 1, "Result", 101),
            Finding("results.no-gender", 1, "Result", 102),
            Finding("results.no-gender", 1, "Result", 103, resolution: "accepted"), // принятая — не считается
            Finding("swimmers.dedup-sure", 1, "Swimmer", 1),                       // по пловцу — не считается
            Finding("x.info", 0, "Competition", 1));                                // Info — не считается
        await db.SaveChangesAsync();
        return db;
    }

    private static Task<UnifiedCompetitionList> Get(SwimmDbContext db, string? filter = null) =>
        new CompetitionAdminRepository(db).GetUnifiedAsync(null, null, null, null, showSynthetic: false,
            null, 1, 20, qualityFilter: filter, today: Today);

    private static string Name(UnifiedCompetitionRowDto u) => u.Db!.Single?.Name ?? u.Db.EventName ?? "";

    [Fact]
    public async Task Badge_CountsCompetitionAndResultFindings_EventSumsDays_IgnoresSwimmerAcceptedInfo()
    {
        await using var db = await SeedAsync(nameof(Badge_CountsCompetitionAndResultFindings_EventSumsDays_IgnoresSwimmerAcceptedInfo));
        var rows = (await Get(db)).Page.Items;

        var err = rows.Single(u => Name(u) == "С ошибкой");
        Assert.Equal((1, 0), CompetitionProblemRules.Totals(err.Health));
        Assert.Single(err.HealthFindings);

        var ev = rows.Single(u => u.Db!.IsEvent);
        Assert.Equal([new CompetitionHealthCount("results.no-gender", 1, 3)], ev.Health);
        Assert.Equal(3, ev.HealthFindings.Count);

        Assert.Empty(rows.Single(u => Name(u) == "Чистый").Health);
        Assert.Empty(rows.Single(u => Name(u) == "Только пловец").Health);
    }

    [Fact]
    public async Task ProblemsFilter_KeepsOnlyRowsWithProblems_AcrossSeasons()
    {
        await using var db = await SeedAsync(nameof(ProblemsFilter_KeepsOnlyRowsWithProblems_AcrossSeasons));
        var list = await Get(db, CompetitionProblemRules.Filter);

        Assert.Equal(2, list.Page.TotalCount);
        Assert.All(list.Page.Items, u => Assert.True(CompetitionProblemRules.HasProblem(u)));
        Assert.Contains(list.Page.Items, u => u.Db!.IsEvent); // сезон 23/24 — фильтр не привязан к сезону
    }

    [Fact]
    public async Task CompetitionEventFinding_GoesToEventRow_RelayFinding_GoesToItsCompetition()
    {
        await using var db = await SeedAsync(nameof(CompetitionEventFinding_GoesToEventRow_RelayFinding_GoesToItsCompetition));
        db.Relays.Add(new Relay { Id = 77, TeamName = "T" });
        db.Results.Add(new ResultRecord { Id = 104, CompetitionId = 1, RelayId = 77 });
        db.DataCheckFindings.AddRange(
            Finding("competitions.duplicate-day", 1, "CompetitionEvent", 9),
            Finding("relays.empty", 1, "Relay", 77));
        await db.SaveChangesAsync();

        var rows = (await Get(db)).Page.Items;
        var ev = rows.Single(u => u.Db!.IsEvent);
        Assert.Contains(new CompetitionHealthCount("competitions.duplicate-day", 1, 1), ev.Health);
        Assert.Contains(ev.HealthFindings, f => f.CheckId == "competitions.duplicate-day");
        var clean = rows.Single(u => Name(u) == "Чистый");
        Assert.Equal([new CompetitionHealthCount("relays.empty", 1, 1)], clean.Health);
        Assert.Single(clean.HealthFindings);
    }

    private static AdminAudit SplitAudit(int compId, string kind, DateTime at) => new()
    {
        ActorName = SplitAutoAttachRules.ActorName, Action = SplitAutoAttachRules.AuditAction, EntityType = "Competition",
        EntityId = compId.ToString(), Summary = kind, CreatedAt = at,
        DetailsJson = $"{{\"Trigger\":\"import\",\"Kind\":\"{kind}\"}}",
    };

    [Fact]
    public async Task SplitsRetry_LatestAuditDecides_AndRunningAttachIsNotAProblem()
    {
        await using var db = await SeedAsync(nameof(SplitsRetry_LatestAuditDecides_AndRunningAttachIsNotAProblem));
        db.Competitions.Find(1)!.OrgCompId = 5001;
        db.AdminAudits.AddRange(
            SplitAudit(1, "attached", new DateTime(2026, 10, 11)),
            SplitAudit(1, "failed", new DateTime(2026, 10, 12)),    // последняя — сбой
            SplitAudit(5, "failed", new DateTime(2026, 10, 12)),
            SplitAudit(5, "attached", new DateTime(2026, 10, 13))); // последняя — доклеено
        await db.SaveChangesAsync();
        var repo = new CompetitionAdminRepository(db);

        var list = await repo.GetUnifiedAsync(null, null, null, null, false, null, 1, 20,
            qualityFilter: CompetitionProblemRules.Filter, today: Today);
        var clean = list.Page.Items.Single(u => Name(u) == "Чистый");
        Assert.True(clean.SplitsNeedRetry);
        Assert.Equal([CompetitionProblemKind.SplitsRetry], CompetitionProblemRules.Reasons(CompetitionProblemRules.From(clean)));
        Assert.DoesNotContain(list.Page.Items, u => Name(u) == "Только пловец");
        Assert.NotNull(list.SplitNotes);

        // Доклейка идёт прямо сейчас (compID в очереди) — не проблема ни для фильтра, ни для строки.
        var running = await repo.GetUnifiedAsync(null, null, null, null, false, null, 1, 20,
            qualityFilter: CompetitionProblemRules.Filter, today: Today, splitsPendingOrgCompIds: new HashSet<int> { 5001 });
        Assert.DoesNotContain(running.Page.Items, u => Name(u) == "Чистый");
        var all = await repo.GetUnifiedAsync(null, null, null, null, false, null, 1, 20,
            today: Today, splitsPendingOrgCompIds: new HashSet<int> { 5001 });
        Assert.False(all.Page.Items.Single(u => Name(u) == "Чистый").SplitsNeedRetry);
    }

    [Fact]
    public async Task ProblemsFilter_CombinesWithState()
    {
        await using var db = await SeedAsync(nameof(ProblemsFilter_CombinesWithState));
        var repo = new CompetitionAdminRepository(db);

        var closed = await repo.GetUnifiedAsync(null, null, null, null, false, null, 1, 20,
            qualityFilter: CompetitionProblemRules.Filter, state: CompetitionStateFilters.Closed, today: Today);
        Assert.Equal(2, closed.Page.TotalCount);
        var waiting = await repo.GetUnifiedAsync(null, null, null, null, false, null, 1, 20,
            qualityFilter: CompetitionProblemRules.Filter, state: CompetitionStateFilters.Waiting, today: Today);
        Assert.Equal(0, waiting.Page.TotalCount);
        // Сочетание с поиском (?q=) — в CompetitionProblemsPgTests: поиск идёт через ILIKE, у InMemory его нет.
    }

    [Fact]
    public async Task NoDecoration_ForServiceCalls_NoHealthNoJournal()
    {
        await using var db = await SeedAsync(nameof(NoDecoration_ForServiceCalls_NoHealthNoJournal));
        var list = await new CompetitionAdminRepository(db).GetUnifiedAsync(null, null, null, null, false, null, 1, 20,
            today: Today, unpaged: true, decorate: false);
        Assert.All(list.Page.Items, u => Assert.Empty(u.Health));
        Assert.Null(list.SplitNotes);
    }

    [Fact]
    public async Task TruncatedChecks_ListedUnderProblemsFilter_OnlyForStartEntityTypes()
    {
        await using var db = await SeedAsync(nameof(TruncatedChecks_ListedUnderProblemsFilter_OnlyForStartEntityTypes));
        db.DataCheckStates.AddRange(
            new DataCheckState { CheckId = "results.no-gender", Severity = 1, Total = 120, Shown = 50 },
            new DataCheckState { CheckId = "swimmers.dedup-sure", Severity = 1, Total = 90, Shown = 50 },  // по пловцу
            new DataCheckState { CheckId = "results.merged-sessions", Severity = 2, Total = 1, Shown = 1 }); // не срезана
        await db.SaveChangesAsync();

        var list = await Get(db, CompetitionProblemRules.Filter);
        Assert.Equal([new TruncatedCheckDto("results.no-gender", 50, 120)], list.TruncatedChecks);
        Assert.Empty((await Get(db)).TruncatedChecks);
    }
}
