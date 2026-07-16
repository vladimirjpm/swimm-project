using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
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
}
