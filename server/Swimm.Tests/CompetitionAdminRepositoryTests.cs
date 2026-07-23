using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

public class CompetitionAdminRepositoryTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static DbContextOptions<SwimmDbContext> BuildOptions(string name) =>
        new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static SwimmDbContext CreateDb(string name) =>
        new SwimmDbContext(BuildOptions(name));

    /// <summary>ICacheService, который всегда возвращает miss — репозиторий идёт в БД.</summary>
    private sealed class NullCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static ICacheService NoCache() => new NullCacheService();

    private static CompetitionInputDto ValidInput(string poolType) => new()
    {
        Name = "TestComp",
        Date = "01/01/2024",
        PoolType = poolType,
        Country = "ISR",
        CategoryKeys = []
    };

    // ── тесты ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_RejectsInvalidPoolType()
    {
        await using var db = CreateDb(nameof(Create_RejectsInvalidPoolType));
        var repo = new CompetitionAdminRepository(db, NoCache());

        var result = await repo.CreateAsync(ValidInput("50 m"));

        Assert.False(result.Success);
        Assert.Contains("бассейн", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Competitions);
    }

    [Fact]
    public async Task Create_RejectsEmptyPoolType()
    {
        await using var db = CreateDb(nameof(Create_RejectsEmptyPoolType));
        var repo = new CompetitionAdminRepository(db, NoCache());

        var result = await repo.CreateAsync(ValidInput(""));

        Assert.False(result.Success);
        Assert.Contains("бассейн", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Competitions);
    }

    [Fact]
    public async Task Create_AcceptsCanonicalPoolType()
    {
        await using var db = CreateDb(nameof(Create_AcceptsCanonicalPoolType));
        var repo = new CompetitionAdminRepository(db, NoCache());

        var result = await repo.CreateAsync(ValidInput("50m"));

        Assert.True(result.Success);
        Assert.Single(db.Competitions);
    }

    // ── GetUnifiedAsync (объединённый список Competitions + Discovery) ──────────

    [Fact]
    public async Task GetUnified_AssignsStagesAcrossSources()
    {
        await using var db = CreateDb(nameof(GetUnified_AssignsStagesAcrossSources));
        // Imported: соревнование со штампом OrgCompId + discovery-строка с тем же compID.
        db.Competitions.Add(new Competition { Id = 1, Name = "Imported comp", Date = "05/07/2026", PoolType = "50m", OrgCompId = 100 });
        // DbOnly: соревнование без OrgCompId, ни одна discovery-строка на него не матчится.
        db.Competitions.Add(new Competition { Id = 2, Name = "PDF only comp", Date = "01/01/2020", PoolType = "25m" });
        // Синтетика: по умолчанию должна быть скрыта.
        db.Competitions.Add(new Competition { Id = 3, Name = "SYNTH Meet 0001", Date = "07/01/2016", PoolType = "50m" });
        db.DiscoveredCompetitions.AddRange(
            new DiscoveredCompetition
            {
                Id = 10, OrgCompId = 100, Name = "Imported comp",
                DateStart = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
                DateEnd = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc), Status = "imported"
            },
            new DiscoveredCompetition // OnSite: на сайте есть, в БД нет
            {
                Id = 11, OrgCompId = 200, Name = "Future site comp",
                DateStart = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                DateEnd = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), Status = "new"
            },
            new DiscoveredCompetition // Ignored
            {
                Id = 12, OrgCompId = 300, Name = "Hidden site comp",
                DateStart = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                DateEnd = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), Status = "ignored"
            });
        await db.SaveChangesAsync();

        var repo = new CompetitionAdminRepository(db, NoCache());
        var all = await repo.GetUnifiedAsync(null, null, null, null, showSynthetic: false, month: null, 1, 20);

        // SYNTH скрыта по умолчанию → 4 (imported/dbOnly/onSite/ignored), без синтетики.
        Assert.Equal(4, all.Page.TotalCount);
        var byStage = all.Page.Items.GroupBy(u => u.Stage).ToDictionary(g => g.Key, g => g.ToList());
        Assert.Single(byStage[CompetitionStage.Imported]);
        Assert.Single(byStage[CompetitionStage.DbOnly]);
        Assert.Single(byStage[CompetitionStage.OnSite]);
        Assert.Single(byStage[CompetitionStage.Ignored]);

        // Imported-строка несёт обе стороны; site-оверлей — та самая discovery-строка.
        var imported = byStage[CompetitionStage.Imported][0];
        Assert.NotNull(imported.Db);
        Assert.Equal(100, imported.Site!.OrgCompId);

        // Счётчики по месяцам: imported (05.07), onSite (01.08), ignored (01.03), dbOnly (01.01).
        Assert.Equal(1, all.MonthCounts[6]);  // июль
        Assert.Equal(1, all.MonthCounts[7]);  // август
        Assert.Equal(1, all.MonthCounts[2]);  // март
        Assert.Equal(1, all.MonthCounts[0]);  // январь

        // Фильтр по месяцу: только июльская (imported) строка.
        var july = await repo.GetUnifiedAsync(null, null, null, null, showSynthetic: false, month: 7, 1, 20);
        Assert.Equal(1, july.Page.TotalCount);
        Assert.Equal(CompetitionStage.Imported, july.Page.Items[0].Stage);

        // Фильтр по стадии.
        var onSiteOnly = await repo.GetUnifiedAsync(null, null, null, "OnSite", showSynthetic: false, month: null, 1, 20);
        Assert.Equal(1, onSiteOnly.Page.TotalCount);
        Assert.Equal(200, onSiteOnly.Page.Items[0].Site!.OrgCompId);

        // Показ синтетики — SYNTH-строка появляется (DbOnly).
        var withSynth = await repo.GetUnifiedAsync(null, null, null, null, showSynthetic: true, month: null, 1, 20);
        Assert.Equal(5, withSynth.Page.TotalCount);
        Assert.Contains(withSynth.Page.Items, u => u.Db?.Single?.Name == "SYNTH Meet 0001");
    }
}
