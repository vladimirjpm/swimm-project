using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>Админский CRUD стилей (StyleAdminRepository): защита посевных, дубли, удаление.</summary>
public class StyleAdminRepositoryTests
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

    private static StyleAdminRepository Repo(SwimmDbContext db) => new(db, new NullCache());

    [Fact]
    public async Task Create_AddsStyle()
    {
        await using var db = CreateDb(nameof(Create_AddsStyle));
        var res = await Repo(db).CreateAsync(new StyleInputDto { Name = "  sidestroke  " });

        Assert.True(res.Success);
        Assert.Equal("sidestroke", (await db.Styles.SingleAsync(s => s.Id == res.Id)).Name);
    }

    [Fact]
    public async Task Create_DuplicateName_Fails()
    {
        await using var db = CreateDb(nameof(Create_DuplicateName_Fails));
        db.Styles.Add(new Style { Name = "sidestroke" });
        await db.SaveChangesAsync();

        var res = await Repo(db).CreateAsync(new StyleInputDto { Name = "sidestroke" });
        Assert.False(res.Success);
    }

    [Fact]
    public async Task Rename_ReservedStyle_Blocked()
    {
        await using var db = CreateDb(nameof(Rename_ReservedStyle_Blocked));
        var style = new Style { Name = "freestyle" };
        db.Styles.Add(style);
        await db.SaveChangesAsync();

        var res = await Repo(db).UpdateAsync(style.Id, new StyleInputDto { Name = "free" });
        Assert.False(res.Success);
        Assert.Equal("freestyle", (await db.Styles.SingleAsync()).Name);
    }

    [Fact]
    public async Task Delete_ReservedStyle_Blocked()
    {
        await using var db = CreateDb(nameof(Delete_ReservedStyle_Blocked));
        var style = new Style { Name = "butterfly" };
        db.Styles.Add(style);
        await db.SaveChangesAsync();

        var res = await Repo(db).DeleteAsync(style.Id);
        Assert.False(res.Success);
        Assert.NotNull(await db.Styles.FindAsync(style.Id));
    }

    [Fact]
    public async Task Delete_StyleWithResults_Blocked()
    {
        await using var db = CreateDb(nameof(Delete_StyleWithResults_Blocked));
        var style = new Style { Name = "custom_medley" };
        var swimmer = new Swimmer { LastName = "A", FirstName = "X" };
        var comp = new Competition { Name = "Meet", Date = "01/06/2026", PoolType = "25m" };
        var club = new Club { Name = "Club" };
        db.AddRange(style, swimmer, comp, club);
        db.Results.Add(new ResultRecord
        {
            Swimmer = swimmer, Competition = comp, Club = club, Style = style,
            Distance = "50", Gender = "male", CompetitionDate = new DateTime(2026, 6, 1)
        });
        await db.SaveChangesAsync();

        var res = await Repo(db).DeleteAsync(style.Id);
        Assert.False(res.Success);
        Assert.NotNull(await db.Styles.FindAsync(style.Id));
    }

    [Fact]
    public async Task Delete_UnusedCustomStyle_Succeeds()
    {
        await using var db = CreateDb(nameof(Delete_UnusedCustomStyle_Succeeds));
        var style = new Style { Name = "custom_unused" };
        db.Styles.Add(style);
        await db.SaveChangesAsync();

        var res = await Repo(db).DeleteAsync(style.Id);
        Assert.True(res.Success);
        Assert.Null(await db.Styles.FindAsync(style.Id));
    }
}
