using System;
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
}
