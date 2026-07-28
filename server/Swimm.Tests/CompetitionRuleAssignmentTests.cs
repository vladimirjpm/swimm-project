using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Привязка правил очков к соревнованиям (Э4): выборка под массовую простановку,
/// «не менять» vs «сбросить в авто», гард на несуществующее правило, дни события.
/// </summary>
public class CompetitionRuleAssignmentTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static CompetitionAdminRepository Repo(SwimmDbContext db) => new(db, new NullCache());

    /// <summary>Два правила (клубное #1, пловца #1) + соревнования по вкусу теста.</summary>
    private static async Task SeedRulesAsync(SwimmDbContext db)
    {
        db.PointRulesClubs.Add(new PointRuleClubs
        {
            Id = 1, Version = "2026.01", Scope = "all", EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        db.PointRulesSwimmers.Add(new PointRuleSwimmers
        {
            Id = 1, Version = "2026.01-hp", Scope = "all", EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Assign_SetsBothRules()
    {
        await using var db = CreateDb(nameof(Assign_SetsBothRules));
        await SeedRulesAsync(db);
        db.Competitions.Add(new Competition { Id = 10, Name = "A", Date = "01/02/2026", PoolType = "50m" });
        await db.SaveChangesAsync();

        var res = await Repo(db).AssignRulesAsync(new CompetitionRuleAssignmentDto
        {
            CompetitionIds = [10],
            SetClubs = true, ClubsRuleId = 1,
            SetSwimmers = true, SwimmersRuleId = 1
        });

        Assert.True(res.Success);
        Assert.Equal(1, res.Id); // Id массовой операции = число изменённых строк
        var comp = await db.Competitions.FindAsync(10);
        Assert.Equal(1, comp!.PointRuleClubsId);
        Assert.Equal(1, comp.PointRuleSwimmersId);
    }

    [Fact]
    public async Task Assign_KeepsUntouchedField()
    {
        await using var db = CreateDb(nameof(Assign_KeepsUntouchedField));
        await SeedRulesAsync(db);
        db.Competitions.Add(new Competition
        {
            Id = 10, Name = "A", Date = "01/02/2026", PoolType = "50m", PointRuleSwimmersId = 1
        });
        await db.SaveChangesAsync();

        var res = await Repo(db).AssignRulesAsync(new CompetitionRuleAssignmentDto
        {
            CompetitionIds = [10],
            SetClubs = true, ClubsRuleId = 1,
            SetSwimmers = false
        });

        Assert.True(res.Success);
        var comp = await db.Competitions.FindAsync(10);
        Assert.Equal(1, comp!.PointRuleClubsId);
        Assert.Equal(1, comp.PointRuleSwimmersId); // не тронуто
    }

    [Fact]
    public async Task Assign_NullRuleId_ClearsBinding()
    {
        await using var db = CreateDb(nameof(Assign_NullRuleId_ClearsBinding));
        await SeedRulesAsync(db);
        db.Competitions.Add(new Competition
        {
            Id = 10, Name = "A", Date = "01/02/2026", PoolType = "50m", PointRuleClubsId = 1
        });
        await db.SaveChangesAsync();

        var res = await Repo(db).AssignRulesAsync(new CompetitionRuleAssignmentDto
        {
            CompetitionIds = [10],
            SetClubs = true, ClubsRuleId = null
        });

        Assert.True(res.Success);
        Assert.Null((await db.Competitions.FindAsync(10))!.PointRuleClubsId);
    }

    [Fact]
    public async Task Assign_RejectsMissingRule()
    {
        await using var db = CreateDb(nameof(Assign_RejectsMissingRule));
        await SeedRulesAsync(db);
        db.Competitions.Add(new Competition { Id = 10, Name = "A", Date = "01/02/2026", PoolType = "50m" });
        await db.SaveChangesAsync();

        var res = await Repo(db).AssignRulesAsync(new CompetitionRuleAssignmentDto
        {
            CompetitionIds = [10],
            SetClubs = true, ClubsRuleId = 999
        });

        Assert.False(res.Success);
        Assert.Contains("не найдено", res.Error);
        Assert.Null((await db.Competitions.FindAsync(10))!.PointRuleClubsId);
    }

    [Fact]
    public async Task Assign_RejectsEmptySelectionAndNoChange()
    {
        await using var db = CreateDb(nameof(Assign_RejectsEmptySelectionAndNoChange));
        await SeedRulesAsync(db);

        var noRows = await Repo(db).AssignRulesAsync(new CompetitionRuleAssignmentDto
        {
            CompetitionIds = [], SetClubs = true, ClubsRuleId = 1
        });
        Assert.False(noRows.Success);

        var noChange = await Repo(db).AssignRulesAsync(new CompetitionRuleAssignmentDto
        {
            CompetitionIds = [10]
        });
        Assert.False(noChange.Success);
    }

    [Fact]
    public async Task Update_RejectsMissingRule()
    {
        await using var db = CreateDb(nameof(Update_RejectsMissingRule));
        await SeedRulesAsync(db);
        db.Competitions.Add(new Competition { Id = 10, Name = "A", Date = "01/02/2026", PoolType = "50m" });
        await db.SaveChangesAsync();

        var res = await Repo(db).UpdateAsync(10, new CompetitionInputDto
        {
            Name = "A", Date = "01/02/2026", PoolType = "50m", PointRuleSwimmersId = 42
        });

        Assert.False(res.Success);
        Assert.Contains("High Point #42", res.Error);
    }

    [Fact]
    public async Task GetForRuleAssignment_FiltersByScopeYearAndUnassigned()
    {
        await using var db = CreateDb(nameof(GetForRuleAssignment_FiltersByScopeYearAndUnassigned));
        await SeedRulesAsync(db);
        db.Competitions.AddRange(
            new Competition { Id = 1, Name = "Обычное 2026", Date = "01/02/2026", PoolType = "50m" },
            new Competition { Id = 2, Name = "Masters 2026", Date = "01/03/2026", PoolType = "50m", IsMasters = true },
            new Competition { Id = 3, Name = "Обычное 2025", Date = "01/02/2025", PoolType = "50m" },
            new Competition { Id = 4, Name = "Привязанное 2026", Date = "01/04/2026", PoolType = "50m", PointRuleClubsId = 1 },
            new Competition { Id = 5, Name = "SYNTH Meet 0001", Date = "01/05/2026", PoolType = "50m" });
        await db.SaveChangesAsync();
        var repo = Repo(db);

        var all2026 = await repo.GetForRuleAssignmentAsync(2026, "all", onlyUnassigned: false);
        Assert.Equal([4, 2, 1], all2026.Select(r => r.Id)); // по дате убыв., синтетика скрыта

        var masters = await repo.GetForRuleAssignmentAsync(null, "masters", onlyUnassigned: false);
        Assert.Equal([2], masters.Select(r => r.Id));

        var unassigned = await repo.GetForRuleAssignmentAsync(2026, "all", onlyUnassigned: true);
        Assert.Equal([2, 1], unassigned.Select(r => r.Id));

        var bound = Assert.Single(all2026, r => r.Id == 4);
        Assert.Equal("2026.01", bound.ClubsRuleVersion);
        Assert.Null(bound.SwimmersRuleVersion);
    }

    [Fact]
    public async Task GetEventDayIds_ReturnsAllDaysInOrder()
    {
        await using var db = CreateDb(nameof(GetEventDayIds_ReturnsAllDaysInOrder));
        db.CompetitionEvents.Add(new CompetitionEvent { Id = 7, Name = "Многодневка" });
        db.Competitions.AddRange(
            new Competition { Id = 21, Name = "День 2", Date = "02/02/2026", PoolType = "50m", EventId = 7, DayNumber = 2 },
            new Competition { Id = 20, Name = "День 1", Date = "01/02/2026", PoolType = "50m", EventId = 7, DayNumber = 1 },
            new Competition { Id = 30, Name = "Чужое", Date = "01/02/2026", PoolType = "50m" });
        await db.SaveChangesAsync();

        var ids = await Repo(db).GetEventDayIdsAsync(7);

        Assert.Equal([20, 21], ids);
    }
}
