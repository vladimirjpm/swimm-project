using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Профиль пловца по id (GetSwimmerProfileAsync) для страницы swimmer.html: маппинг
/// клуба/страны, FullName (RU-приоритет, EN-фолбэк), 404 для несуществующего id.
/// </summary>
public class SwimmerProfileRepositoryTests
{
    private static DbContextOptions<SwimmReadDbContext> BuildOptions(string name) =>
        new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static SwimmReadDbContext CreateDb(string name) =>
        new SwimmReadDbContext(BuildOptions(name));

    private sealed class NullCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static IResultRepository Repo(SwimmReadDbContext db) => new ResultRepository(db, new NullCacheService());

    [Fact]
    public async Task Profile_KnownSwimmer_MapsClubCountryAndRuFullName()
    {
        await using var db = CreateDb(nameof(Profile_KnownSwimmer_MapsClubCountryAndRuFullName));
        var club = new Club { Name = "Дельфин", NameEn = "Dolphin" };
        var country = new Country { CountryCode = "ISR", CountryName = "Israel" };
        db.AddRange(club, country);
        await db.SaveChangesAsync();

        var swimmer = new Swimmer
        {
            FirstName = "Иван", LastName = "Иванов",
            FirstNameEn = "Ivan", LastNameEn = "Ivanov",
            BirthYear = 2005, Gender = "M", Origin = "isr",
            ClubId = club.Id, CountryId = country.Id,
        };
        db.Add(swimmer);
        await db.SaveChangesAsync();

        var dto = await Repo(db).GetSwimmerProfileAsync(swimmer.Id);

        Assert.NotNull(dto);
        Assert.Equal("Иван Иванов", dto!.FullName);
        Assert.Equal("Дельфин", dto.ClubName);
        Assert.Equal("ISR", dto.CountryCode);
        Assert.Equal("Israel", dto.CountryName);
        Assert.Equal(2005, dto.BirthYear);
        Assert.Equal("M", dto.Gender);
    }

    [Fact]
    public async Task Profile_RuNameEmpty_FallsBackToEnFullName()
    {
        await using var db = CreateDb(nameof(Profile_RuNameEmpty_FallsBackToEnFullName));
        var swimmer = new Swimmer
        {
            FirstName = "", LastName = "",
            FirstNameEn = "John", LastNameEn = "Doe",
            BirthYear = 1999, Origin = "local",
        };
        db.Add(swimmer);
        await db.SaveChangesAsync();

        var dto = await Repo(db).GetSwimmerProfileAsync(swimmer.Id);

        Assert.NotNull(dto);
        Assert.Equal("John Doe", dto!.FullName);
        Assert.Equal("local", dto.Origin);
        Assert.Null(dto.ClubName);
        Assert.Null(dto.CountryCode);
    }

    [Fact]
    public async Task Profile_UnknownId_ReturnsNull()
    {
        await using var db = CreateDb(nameof(Profile_UnknownId_ReturnsNull));
        Assert.Null(await Repo(db).GetSwimmerProfileAsync(999));
        Assert.Null(await Repo(db).GetSwimmerProfileAsync(0));
        Assert.Null(await Repo(db).GetSwimmerProfileAsync(-5));
    }
}
