using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Пометка «семья» в избранном (<c>UserFavorite.IsFamily</c>, docs/plans/family-favorites-plan.md):
/// только своё, только пловец, доезжает до DTO. Прав не даёт — проверять тут нечего, кроме того,
/// что чужую запись не пометить (IDOR) и клуб семьёй не станет.
/// </summary>
public class UserFavoriteFamilyTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static UserFavoriteRepository Repo(SwimmDbContext db) =>
        new(db, new AdminSettingsService(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

    private static async Task<(AppUser owner, AppUser other, FavoriteDto swimmerFav, FavoriteDto clubFav)> SeedAsync(SwimmDbContext db)
    {
        var owner = new AppUser { Email = "owner@example.com", DisplayName = "Owner", SecurityStamp = "s" };
        var other = new AppUser { Email = "other@example.com", DisplayName = "Other", SecurityStamp = "s" };
        var swimmer = new Swimmer { LastName = "Kid", FirstName = "One", BirthYear = 2014 };
        var club = new Club { Name = "Club", NameEn = "Club" };
        db.AddRange(owner, other, swimmer, club);
        await db.SaveChangesAsync();

        var repo = Repo(db);
        var swimmerFav = (await repo.AddAsync(owner.Id, new AddFavoriteRequest { TargetType = "swimmer", SwimmerId = swimmer.Id })).Favorite!;
        var clubFav = (await repo.AddAsync(owner.Id, new AddFavoriteRequest { TargetType = "club", ClubId = club.Id })).Favorite!;
        return (owner, other, swimmerFav, clubFav);
    }

    [Fact]
    public async Task SetFamily_OwnSwimmer_SetsAndClears_AndShowsInDto()
    {
        await using var db = CreateDb(nameof(SetFamily_OwnSwimmer_SetsAndClears_AndShowsInDto));
        var (owner, _, fav, _) = await SeedAsync(db);
        var repo = Repo(db);
        Assert.False(fav.IsFamily); // новое избранное — не семья

        Assert.True(await repo.SetFamilyAsync(owner.Id, fav.Id, isFamily: true));
        Assert.True((await repo.GetForUserAsync(owner.Id)).Single(f => f.Id == fav.Id).IsFamily);

        Assert.True(await repo.SetFamilyAsync(owner.Id, fav.Id, isFamily: false));
        Assert.False((await repo.GetForUserAsync(owner.Id)).Single(f => f.Id == fav.Id).IsFamily);
    }

    [Fact]
    public async Task SetFamily_SomeoneElsesFavorite_Refused()
    {
        await using var db = CreateDb(nameof(SetFamily_SomeoneElsesFavorite_Refused));
        var (_, other, fav, _) = await SeedAsync(db);

        Assert.False(await Repo(db).SetFamilyAsync(other.Id, fav.Id, isFamily: true));
        Assert.False((await db.UserFavorites.SingleAsync(f => f.Id == fav.Id)).IsFamily);
    }

    [Fact]
    public async Task SetFamily_Club_Refused()
    {
        await using var db = CreateDb(nameof(SetFamily_Club_Refused));
        var (owner, _, _, clubFav) = await SeedAsync(db);

        Assert.False(await Repo(db).SetFamilyAsync(owner.Id, clubFav.Id, isFamily: true));
        Assert.False((await db.UserFavorites.SingleAsync(f => f.Id == clubFav.Id)).IsFamily);
    }
}
