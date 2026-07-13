using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Фильтр country в <see cref="RecordRepository.GetStandardsAsync"/>: строки с точным
/// совпадением страны + универсальные (Country == "") — на случай, когда рядом появится
/// второй набор нормативов (см. docs/tasks/normatives-country-sonnet.md).
/// </summary>
public class NormativeStandardCountryFilterTests
{
    private static DbContextOptions<SwimmReadDbContext> BuildOptions(string name) =>
        new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static SwimmReadDbContext CreateDb(string name) =>
        new SwimmReadDbContext(BuildOptions(name));

    /// <summary>ICacheService, который всегда возвращает miss — репозиторий идёт в БД.</summary>
    private sealed class NullCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static ICacheService NoCache() => new NullCacheService();

    private static NormativeStandard Standard(string kind, string country, string style = "freestyle", string distance = "50")
        => new()
        {
            Kind     = kind,
            Country  = country,
            Gender   = "male",
            PoolType = "50m",
            Style    = style,
            Distance = distance,
            AgeKey   = "",
            Level    = "MS",
            Time     = "00:30.00"
        };

    private static async Task<SwimmReadDbContext> SeedAsync(string dbName)
    {
        var db = CreateDb(dbName);
        db.NormativeStandards.AddRange(
            Standard("regular", "RUS"),
            Standard("regular", "ISR"),
            Standard("regular", ""),      // универсальный
            Standard("masters", "RUS"));
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Country_ReturnsOwnCountry_PlusUniversal()
    {
        await using var db = await SeedAsync(nameof(Country_ReturnsOwnCountry_PlusUniversal));
        var repo = new RecordRepository(db, NoCache());

        var result = await repo.GetStandardsAsync(country: "RUS");

        Assert.Equal(3, result.Count); // RUS regular + universal regular + RUS masters
        Assert.All(result, s => Assert.True(s.Country is "RUS" or ""));
        Assert.DoesNotContain(result, s => s.Country == "ISR");
    }

    [Fact]
    public async Task Country_WithKind_ReturnsOwnCountry_PlusUniversal_ForThatKind()
    {
        await using var db = await SeedAsync(nameof(Country_WithKind_ReturnsOwnCountry_PlusUniversal_ForThatKind));
        var repo = new RecordRepository(db, NoCache());

        var result = await repo.GetStandardsAsync(kind: "regular", country: "RUS");

        Assert.Equal(2, result.Count); // RUS regular + universal regular
        Assert.Contains(result, s => s.Country == "RUS");
        Assert.Contains(result, s => s.Country == "");
        Assert.All(result, s => Assert.Equal("regular", s.Kind));
    }

    [Fact]
    public async Task ForeignCountry_DoesNotLeak_OnlyUniversalReturned()
    {
        await using var db = await SeedAsync(nameof(ForeignCountry_DoesNotLeak_OnlyUniversalReturned));
        var repo = new RecordRepository(db, NoCache());

        var result = await repo.GetStandardsAsync(country: "ISR");

        Assert.All(result, s => Assert.True(s.Country == "ISR" || s.Country == ""));
        Assert.DoesNotContain(result, s => s.Country == "RUS");
    }

    [Fact]
    public async Task NoCountry_ReturnsEverything_LegacyBehavior()
    {
        await using var db = await SeedAsync(nameof(NoCountry_ReturnsEverything_LegacyBehavior));
        var repo = new RecordRepository(db, NoCache());

        var result = await repo.GetStandardsAsync();

        Assert.Equal(4, result.Count);
    }

    [Fact]
    public async Task Country_IsNormalized_TrimmedAndUppercased()
    {
        await using var db = await SeedAsync(nameof(Country_IsNormalized_TrimmedAndUppercased));
        var repo = new RecordRepository(db, NoCache());

        var result = await repo.GetStandardsAsync(country: " rus ");

        Assert.Equal(3, result.Count); // как в Country_ReturnsOwnCountry_PlusUniversal
        Assert.All(result, s => Assert.True(s.Country is "RUS" or ""));
    }
}
