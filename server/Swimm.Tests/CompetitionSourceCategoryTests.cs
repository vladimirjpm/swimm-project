using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Канонический таб селектора соревнований (`/api/competitions` → category).
/// Возрастная лестница (2026-07-31): kids8_11 = «Kids» (8–11), young11_14 = «Young» (11–14),
/// juniors = «Juniors» (נוער), adults = «Adults» (בוגרים).
/// </summary>
public class CompetitionSourceCategoryTests
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

    /// <summary>Соревнование + членство в категориях + один результат (без него источник не виден).</summary>
    private static async Task SeedCompetitionAsync(
        SwimmReadDbContext db, int id, string name, bool isMasters, params string[] categoryKeys)
    {
        var comp = new Competition
        {
            Id = id, Name = name, Date = "01/06/2026", PoolType = "50m", IsMasters = isMasters,
            Country = new Country { CountryCode = $"IS{id}", CountryName = "ISR" }
        };
        db.Competitions.Add(comp);
        await db.SaveChangesAsync();

        foreach (var key in categoryKeys)
        {
            var cat = await db.Categories.FirstOrDefaultAsync(c => c.Key == key);
            if (cat is null)
            {
                cat = new Category { Key = key, Name = key, DisplayOrder = 1 };
                db.Categories.Add(cat);
                await db.SaveChangesAsync();
            }
            db.CategoryCompetitions.Add(new CategoryCompetition { CompetitionId = id, CategoryId = cat.Id });
        }

        // Справочники общие на всю фикстуру: повторный Add уже сохранённой сущности
        // InMemory-провайдер трактует как вставку с занятым Id.
        var style = await db.Styles.FirstOrDefaultAsync();
        if (style is null) { style = new Style { Name = "freestyle" }; db.Add(style); }
        var club = await db.Clubs.FirstOrDefaultAsync();
        if (club is null) { club = new Club { Name = "Club", NameEn = "Club" }; db.Add(club); }
        var swimmer = await db.Swimmers.FirstOrDefaultAsync();
        if (swimmer is null)
        {
            swimmer = new Swimmer { LastName = "A", FirstName = "A", LastNameEn = "A", FirstNameEn = "A", BirthYear = 2005 };
            db.Add(swimmer);
        }
        await db.SaveChangesAsync();

        db.Results.Add(new ResultRecord
        {
            CompetitionId = id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            Distance = "100", Gender = "male", CompetitionDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeOriginal = "1:00.00", Position = 1, AgeGroup = "Open", EventStyleAge = "100 freestyle Open"
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task MainCategory_GetsItsOwnAdultsTab_NotJunior()
    {
        await using var db = CreateDb(nameof(MainCategory_GetsItsOwnAdultsTab_NotJunior));
        await SeedCompetitionAsync(db, 1, "בוגרים", isMasters: false, "results-main");

        var sources = await new ResultRepository(db, new NullCache()).GetSourcesAsync();

        Assert.Equal("adults", Assert.Single(sources).Category);
    }

    [Fact]
    public async Task EachLadderStepGetsItsOwnTab()
    {
        await using var db = CreateDb(nameof(EachLadderStepGetsItsOwnTab));
        await SeedCompetitionAsync(db, 1, "Kids", isMasters: false, "results-kids-team");
        await SeedCompetitionAsync(db, 2, "Young", isMasters: false, "results-youth-team");
        await SeedCompetitionAsync(db, 3, "Juniors", isMasters: false, "results-junior-results");

        var sources = await new ResultRepository(db, new NullCache()).GetSourcesAsync();

        Assert.Equal("kids8_11", sources.Single(s => s.Id == 1).Category);
        Assert.Equal("young11_14", sources.Single(s => s.Id == 2).Category);
        Assert.Equal("juniors", sources.Single(s => s.Id == 3).Category);
    }

    [Fact]
    public async Task Masters_WinsOverOtherMemberships()
    {
        await using var db = CreateDb(nameof(Masters_WinsOverOtherMemberships));
        await SeedCompetitionAsync(db, 1, "Masters meet", isMasters: false, "results-masters", "results-main");

        var sources = await new ResultRepository(db, new NullCache()).GetSourcesAsync();

        Assert.Equal("masters", Assert.Single(sources).Category);
    }

    [Fact]
    public async Task YoungerStepWins_WhenBothMembershipsPresent()
    {
        // Приоритет: masters > Kids > Young > Juniors > Adults. Соревнование в двух ступенях
        // сразу попадает в более младшую.
        await using var db = CreateDb(nameof(YoungerStepWins_WhenBothMembershipsPresent));
        await SeedCompetitionAsync(db, 1, "Mixed", isMasters: false, "results-junior-results", "results-main");

        var sources = await new ResultRepository(db, new NullCache()).GetSourcesAsync();

        Assert.Equal("juniors", Assert.Single(sources).Category);
    }

    [Fact]
    public async Task CustomCategoryOnly_HasNoCanonicalTab()
    {
        await using var db = CreateDb(nameof(CustomCategoryOnly_HasNoCanonicalTab));
        await SeedCompetitionAsync(db, 1, "Maccabiah", isMasters: false, "result-maccabiah");

        var sources = await new ResultRepository(db, new NullCache()).GetSourcesAsync();

        Assert.Null(Assert.Single(sources).Category);
    }
}
