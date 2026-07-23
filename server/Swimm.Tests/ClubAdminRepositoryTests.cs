using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>Правка клуба (ClubAdminRepository): переименование, пустое имя, ненайденный клуб.</summary>
public class ClubAdminRepositoryTests
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

    private static ClubAdminRepository Repo(SwimmDbContext db) => new(db, new NullCache());

    [Fact]
    public async Task Update_RenamesClub()
    {
        await using var db = CreateDb(nameof(Update_RenamesClub));
        var club = new Club { Name = "Старое", NameEn = "Old" };
        db.Clubs.Add(club);
        await db.SaveChangesAsync();

        var res = await Repo(db).UpdateAsync(club.Id, new ClubInputDto
        {
            Name = "  Новое  ", NameEn = "New", IsPseudo = true
        });

        Assert.True(res.Success);
        var saved = await db.Clubs.SingleAsync();
        Assert.Equal("Новое", saved.Name);
        Assert.Equal("New", saved.NameEn);
        Assert.True(saved.IsPseudo);
    }

    [Fact]
    public async Task Update_EmptyName_Fails()
    {
        await using var db = CreateDb(nameof(Update_EmptyName_Fails));
        var club = new Club { Name = "Клуб" };
        db.Clubs.Add(club);
        await db.SaveChangesAsync();

        var res = await Repo(db).UpdateAsync(club.Id, new ClubInputDto { Name = "   " });
        Assert.False(res.Success);
        Assert.Equal("Клуб", (await db.Clubs.SingleAsync()).Name);
    }

    [Fact]
    public async Task Update_MissingClub_Fails()
    {
        await using var db = CreateDb(nameof(Update_MissingClub_Fails));
        var res = await Repo(db).UpdateAsync(999, new ClubInputDto { Name = "X" });
        Assert.False(res.Success);
    }

    [Fact]
    public async Task GetById_ReturnsResultCount()
    {
        await using var db = CreateDb(nameof(GetById_ReturnsResultCount));
        var club = new Club { Name = "Клуб" };
        var sw = new Swimmer { LastName = "A", FirstName = "X" };
        var comp = new Competition { Name = "Meet", Date = "01/06/2026", PoolType = "25m" };
        var style = new Style { Name = "freestyle" };
        db.AddRange(club, sw, comp, style);
        db.Results.Add(new ResultRecord
        {
            Club = club, Swimmer = sw, Competition = comp, Style = style,
            Distance = "50", Gender = "male", CompetitionDate = new DateTime(2026, 6, 1)
        });
        await db.SaveChangesAsync();

        var d = await Repo(db).GetByIdAsync(club.Id);
        Assert.NotNull(d);
        Assert.Equal(1, d!.ResultCount);
    }
}
