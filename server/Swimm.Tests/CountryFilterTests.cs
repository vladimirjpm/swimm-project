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
/// Фильтр country (alpha-3) в <see cref="ResultRepository.GetSourcesAsync"/> и
/// <see cref="ResultRepository.GetPagedAsync"/> — см. docs/tasks/competitions-country-filter-sonnet.md.
/// Страна многодневного события = страна ЛЮБОГО из его дней.
/// </summary>
public class CountryFilterTests
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

    // ── GetSourcesAsync ──────────────────────────────────────────────────────

    /// <summary>Сеет два однодневных соревнования (ISR/USA) + событие из двух дней (ISR + USA).</summary>
    private static async Task<SwimmReadDbContext> SeedSourcesAsync(string dbName)
    {
        var db = CreateDb(dbName);

        var isr = new Country { CountryCode = "ISR", CountryName = "ISR" };
        var usa = new Country { CountryCode = "USA", CountryName = "USA" };

        db.Competitions.Add(new Competition
        {
            Name = "Israel Open", Country = isr, Date = "01/01/2024", PoolType = "50m"
        });
        db.Competitions.Add(new Competition
        {
            Name = "USA Open", Country = usa, Date = "02/01/2024", PoolType = "50m"
        });

        var evt = new CompetitionEvent
        {
            Name = "Mixed Event",
            StartDate = new DateOnly(2024, 3, 1),
            EndDate = new DateOnly(2024, 3, 2)
        };
        db.CompetitionEvents.Add(evt);
        await db.SaveChangesAsync();

        db.Competitions.Add(new Competition
        {
            Name = "Mixed Event", Country = isr, Date = "01/03/2024", PoolType = "50m",
            EventId = evt.Id, DayNumber = 1
        });
        db.Competitions.Add(new Competition
        {
            Name = "Mixed Event", Country = usa, Date = "02/03/2024", PoolType = "50m",
            EventId = evt.Id, DayNumber = 2
        });
        await db.SaveChangesAsync();

        return db;
    }

    [Fact]
    public async Task GetSources_NoCountry_ReturnsEverything()
    {
        await using var db = await SeedSourcesAsync(nameof(GetSources_NoCountry_ReturnsEverything));
        var repo = new ResultRepository(db, NoCache());

        var result = await repo.GetSourcesAsync();

        Assert.Equal(3, result.Count); // 2 однодневных + 1 событие
    }

    [Fact]
    public async Task GetSources_CountryIsr_ReturnsIsraeliSingle_PlusEventWithIsrDay()
    {
        await using var db = await SeedSourcesAsync(nameof(GetSources_CountryIsr_ReturnsIsraeliSingle_PlusEventWithIsrDay));
        var repo = new ResultRepository(db, NoCache());

        var result = await repo.GetSourcesAsync("ISR");

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.Kind == "competition" && s.Name == "Israel Open");
        Assert.Contains(result, s => s.Kind == "event" && s.Name == "Mixed Event");
        Assert.DoesNotContain(result, s => s.Kind == "competition" && s.Name == "USA Open");
    }

    [Fact]
    public async Task GetSources_ForeignCountry_ReturnsEmpty()
    {
        await using var db = await SeedSourcesAsync(nameof(GetSources_ForeignCountry_ReturnsEmpty));
        var repo = new ResultRepository(db, NoCache());

        var result = await repo.GetSourcesAsync("FRA");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSources_Country_IsNormalized_TrimmedAndUppercased()
    {
        await using var db = await SeedSourcesAsync(nameof(GetSources_Country_IsNormalized_TrimmedAndUppercased));
        var repo = new ResultRepository(db, NoCache());

        var result = await repo.GetSourcesAsync(" isr ");

        Assert.Equal(2, result.Count);
    }

    // ── GetPagedAsync (Country в ResultFilter) ──────────────────────────────

    private static async Task<(Competition Isr, Competition Usa)> SeedResultsAsync(SwimmReadDbContext db)
    {
        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "TestClub", NameEn = "TestClub" };
        var swimmer = new Swimmer
        {
            LastName = "Иванов", FirstName = "Иван",
            LastNameEn = "Ivanov", FirstNameEn = "Ivan",
            BirthYear = 2000
        };
        var compIsr = new Competition { Name = "Israel Open", Country = new Country { CountryCode = "ISR", CountryName = "ISR" }, Date = "01/01/2024", PoolType = "50m" };
        var compUsa = new Competition { Name = "USA Open", Country = new Country { CountryCode = "USA", CountryName = "USA" }, Date = "02/01/2024", PoolType = "50m" };

        db.Styles.Add(style);
        db.Clubs.Add(club);
        db.Swimmers.Add(swimmer);
        db.Competitions.Add(compIsr);
        db.Competitions.Add(compUsa);
        await db.SaveChangesAsync();

        db.Results.Add(new ResultRecord
        {
            CompetitionId = compIsr.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "100", Gender = "male",
            CompetitionDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeOriginal = "1:00.00", AgeGroup = "Open", EventStyleAge = "100 freestyle Open"
        });
        db.Results.Add(new ResultRecord
        {
            CompetitionId = compUsa.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "200", Gender = "male",
            CompetitionDate = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            TimeOriginal = "2:00.00", AgeGroup = "Open", EventStyleAge = "200 freestyle Open"
        });
        await db.SaveChangesAsync();

        return (compIsr, compUsa);
    }

    [Fact]
    public async Task GetPaged_FilterByCountry_ReturnsOnlyThatCountrysResults()
    {
        await using var db = CreateDb(nameof(GetPaged_FilterByCountry_ReturnsOnlyThatCountrysResults));
        await SeedResultsAsync(db);
        var repo = new ResultRepository(db, NoCache());

        var (items, _, total) = await repo.GetPagedAsync(new ResultFilter { Country = "ISR" }, 1, 10);

        Assert.Single(items);
        Assert.Equal(1, total);
        Assert.Equal("100", items[0].Distance);
    }

    [Fact]
    public async Task GetPaged_NoCountry_ReturnsAll()
    {
        await using var db = CreateDb(nameof(GetPaged_NoCountry_ReturnsAll));
        await SeedResultsAsync(db);
        var repo = new ResultRepository(db, NoCache());

        var (items, _, total) = await repo.GetPagedAsync(new ResultFilter(), 1, 10);

        Assert.Equal(2, items.Count);
        Assert.Equal(2, total);
    }
}
