using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Выдача правил клубных очков клиенту (<c>GET /api/club-points</c>). Клиент подбирает
/// правило сам, по дате и scope — значит видеть он должен ровно то, что участвует в
/// автоподборе на сервере (см. docs/competition-overview-cards.md, раздел Top clubs).
/// </summary>
public class ClubPointsRepositoryTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
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

    [Fact]
    public async Task GetRules_HidesManualOnlyRules()
    {
        await using var db = CreateDb(nameof(GetRules_HidesManualOnlyRules));
        db.PointRulesClubs.AddRange(
            new PointRuleClubs
            {
                Id = 1, Version = "2025.01", Scope = "all",
                EffectiveFrom = new DateOnly(2025, 1, 1),
                Entries = [new PointRuleClubsEntry { Place = 1, Points = 30 }]
            },
            // Свежее по дате, но только для явной привязки: клиент его выбрал бы вместо
            // привязанного и посчитал очки по чужой шкале.
            new PointRuleClubs
            {
                Id = 2, Version = "2026.01-youth-11-14", Scope = "all", ManualOnly = true,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                Entries = [new PointRuleClubsEntry { Place = 1, Points = 40 }]
            });
        await db.SaveChangesAsync();

        var rules = await new ClubPointsRepository(db, new NullCache()).GetRulesAsync();

        var rule = Assert.Single(rules);
        Assert.Equal("2025.01", rule.Version);
        Assert.Equal(30, rule.PointsByPlace["1"]);
    }
}
