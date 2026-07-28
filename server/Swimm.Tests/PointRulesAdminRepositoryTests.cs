using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Validation;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Админский CRUD правил очков (Э3, /Admin/PointsRules): шкала текстом, гарды удаления,
/// уникальность версии, независимость двух видов правил.
/// </summary>
public class PointRulesAdminRepositoryTests
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

    private static PointRulesAdminRepository Repo(SwimmDbContext db) => new(db, new NullCache());

    private static PointRuleInputDto Input(string version, params int[] points) => new()
    {
        Version = version,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        Scope = "all",
        Entries = points.Select((p, i) => new PointRuleEntryDto { Place = i + 1, Points = p }).ToList()
    };

    // ── шкала текстом ─────────────────────────────────────────────────────────

    [Fact]
    public void ScaleText_ParsesCommaList_AsPlacesFromOne()
    {
        Assert.True(PointRuleScaleText.TryParse("30, 28,26", out var entries, out var error));
        Assert.Null(error);
        Assert.Equal([(1, 30), (2, 28), (3, 26)], entries.Select(e => (e.Place, e.Points)));
    }

    [Fact]
    public void ScaleText_ParsesExplicitPlaces_AndSorts()
    {
        Assert.True(PointRuleScaleText.TryParse("5 = 10\n1: 30", out var entries, out _));
        Assert.Equal([(1, 30), (5, 10)], entries.Select(e => (e.Place, e.Points)));
    }

    [Fact]
    public void ScaleText_ContinuesNumberingAfterExplicitPlace()
    {
        Assert.True(PointRuleScaleText.TryParse("3=10, 9, 8", out var entries, out _));
        Assert.Equal([(3, 10), (4, 9), (5, 8)], entries.Select(e => (e.Place, e.Points)));
    }

    [Fact]
    public void ScaleText_RejectsDuplicatePlace()
    {
        Assert.False(PointRuleScaleText.TryParse("1=30, 1=28", out _, out var error));
        Assert.Contains("дважды", error);
    }

    [Fact]
    public void ScaleText_RejectsGarbage()
    {
        Assert.False(PointRuleScaleText.TryParse("тридцать", out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void ScaleText_EmptyInput_IsEmptyScale()
    {
        Assert.True(PointRuleScaleText.TryParse("  ", out var entries, out _));
        Assert.Empty(entries);
    }

    [Fact]
    public void ScaleText_FormatsContiguousAsList_AndSparseAsLines()
    {
        var contiguous = new List<PointRuleEntryDto>
        {
            new() { Place = 1, Points = 30 }, new() { Place = 2, Points = 28 }
        };
        Assert.Equal("30, 28", PointRuleScaleText.Format(contiguous));

        var sparse = new List<PointRuleEntryDto>
        {
            new() { Place = 1, Points = 30 }, new() { Place = 7, Points = 5 }
        };
        Assert.Equal("1 = 30\n7 = 5", PointRuleScaleText.Format(sparse));
    }

    [Fact]
    public void ScaleText_RoundTrips()
    {
        Assert.True(PointRuleScaleText.TryParse("30, 28, 26", out var entries, out _));
        Assert.Equal("30, 28, 26", PointRuleScaleText.Format(entries));
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_StoresRuleWithScale()
    {
        await using var db = CreateDb(nameof(Create_StoresRuleWithScale));

        var res = await Repo(db).CreateAsync(PointRuleKind.Clubs, Input("2026.01", 30, 28, 26));
        Assert.True(res.Success);

        var saved = await Repo(db).GetByIdAsync(PointRuleKind.Clubs, res.Id);
        Assert.NotNull(saved);
        Assert.Equal("2026.01", saved!.Version);
        Assert.Equal(3, saved.Entries.Count);
        Assert.Equal(30, saved.Entries[0].Points);
    }

    [Fact]
    public async Task Create_RejectsDuplicateVersion_WithinSameKind()
    {
        await using var db = CreateDb(nameof(Create_RejectsDuplicateVersion_WithinSameKind));
        var repo = Repo(db);

        Assert.True((await repo.CreateAsync(PointRuleKind.Clubs, Input("2026.01", 30))).Success);
        var dup = await repo.CreateAsync(PointRuleKind.Clubs, Input("2026.01", 30));

        Assert.False(dup.Success);
        Assert.Contains("занята", dup.Error);
    }

    [Fact]
    public async Task Create_AllowsSameVersion_InOtherKind()
    {
        await using var db = CreateDb(nameof(Create_AllowsSameVersion_InOtherKind));
        var repo = Repo(db);

        Assert.True((await repo.CreateAsync(PointRuleKind.Clubs, Input("2026.01", 30))).Success);
        Assert.True((await repo.CreateAsync(PointRuleKind.Swimmers, Input("2026.01", 13))).Success);
    }

    [Fact]
    public async Task Create_RejectsBadScope()
    {
        await using var db = CreateDb(nameof(Create_RejectsBadScope));
        var input = Input("2026.01", 30);
        input.Scope = "everyone";

        var res = await Repo(db).CreateAsync(PointRuleKind.Clubs, input);
        Assert.False(res.Success);
        Assert.Contains("scope", res.Error);
    }

    [Fact]
    public async Task Create_Swimmers_RejectsBadPointsSourceAndGroupBy()
    {
        await using var db = CreateDb(nameof(Create_Swimmers_RejectsBadPointsSourceAndGroupBy));
        var repo = Repo(db);

        var badSource = Input("2026.01", 13);
        badSource.PointsSource = "magic";
        Assert.False((await repo.CreateAsync(PointRuleKind.Swimmers, badSource)).Success);

        var badGroup = Input("2026.02", 13);
        badGroup.GroupBy = "club";
        Assert.False((await repo.CreateAsync(PointRuleKind.Swimmers, badGroup)).Success);
    }

    [Fact]
    public async Task Update_RewritesScaleCompletely()
    {
        await using var db = CreateDb(nameof(Update_RewritesScaleCompletely));
        var repo = Repo(db);

        var created = await repo.CreateAsync(PointRuleKind.Swimmers, Input("2026.01", 13, 11, 10));
        var res = await repo.UpdateAsync(PointRuleKind.Swimmers, created.Id, Input("2026.01", 20, 18));

        Assert.True(res.Success);
        var saved = await repo.GetByIdAsync(PointRuleKind.Swimmers, created.Id);
        Assert.Equal([(1, 20), (2, 18)], saved!.Entries.Select(e => (e.Place, e.Points)));
    }

    [Fact]
    public async Task Delete_BlockedWhileCompetitionsReferenceRule()
    {
        await using var db = CreateDb(nameof(Delete_BlockedWhileCompetitionsReferenceRule));
        var repo = Repo(db);

        var created = await repo.CreateAsync(PointRuleKind.Clubs, Input("2026.01", 30));
        db.Competitions.Add(new Competition
        {
            Name = "Тест",
            Date = "01/02/2026",
            PointRuleClubsId = created.Id
        });
        await db.SaveChangesAsync();

        var res = await repo.DeleteAsync(PointRuleKind.Clubs, created.Id);

        Assert.False(res.Success);
        Assert.Contains("ссылаются соревнования", res.Error);
        Assert.NotNull(await repo.GetByIdAsync(PointRuleKind.Clubs, created.Id));
    }

    [Fact]
    public async Task Delete_RemovesUnusedRule()
    {
        await using var db = CreateDb(nameof(Delete_RemovesUnusedRule));
        var repo = Repo(db);

        var created = await repo.CreateAsync(PointRuleKind.Clubs, Input("2026.01", 30));
        var res = await repo.DeleteAsync(PointRuleKind.Clubs, created.Id);

        Assert.True(res.Success);
        Assert.Null(await repo.GetByIdAsync(PointRuleKind.Clubs, created.Id));
    }

    [Fact]
    public async Task GetAll_CountsEntriesAndBoundCompetitions()
    {
        await using var db = CreateDb(nameof(GetAll_CountsEntriesAndBoundCompetitions));
        var repo = Repo(db);

        var created = await repo.CreateAsync(PointRuleKind.Clubs, Input("2026.01", 30, 28));
        db.Competitions.Add(new Competition { Name = "A", Date = "01/02/2026", PointRuleClubsId = created.Id });
        db.Competitions.Add(new Competition { Name = "B", Date = "01/03/2026", PointRuleClubsId = created.Id });
        db.Competitions.Add(new Competition { Name = "C", Date = "01/04/2026" });
        await db.SaveChangesAsync();

        var row = Assert.Single(await repo.GetAllAsync(PointRuleKind.Clubs));
        Assert.Equal(2, row.EntryCount);
        Assert.Equal(2, row.CompetitionCount);
    }
}
