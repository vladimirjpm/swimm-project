using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
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

        Assert.Equal(SetFamilyStatus.Done, await repo.SetFamilyAsync(owner.Id, fav.Id, isFamily: true));
        Assert.True((await repo.GetForUserAsync(owner.Id)).Single(f => f.Id == fav.Id).IsFamily);

        Assert.Equal(SetFamilyStatus.Done, await repo.SetFamilyAsync(owner.Id, fav.Id, isFamily: false));
        Assert.False((await repo.GetForUserAsync(owner.Id)).Single(f => f.Id == fav.Id).IsFamily);
    }

    [Fact]
    public async Task SetFamily_SomeoneElsesFavorite_Refused()
    {
        await using var db = CreateDb(nameof(SetFamily_SomeoneElsesFavorite_Refused));
        var (_, other, fav, _) = await SeedAsync(db);

        Assert.Equal(SetFamilyStatus.NotFound, await Repo(db).SetFamilyAsync(other.Id, fav.Id, isFamily: true));
        Assert.False((await db.UserFavorites.SingleAsync(f => f.Id == fav.Id)).IsFamily);
    }

    [Fact]
    public async Task SetFamily_Club_Refused()
    {
        await using var db = CreateDb(nameof(SetFamily_Club_Refused));
        var (owner, _, _, clubFav) = await SeedAsync(db);

        Assert.Equal(SetFamilyStatus.NotFound, await Repo(db).SetFamilyAsync(owner.Id, clubFav.Id, isFamily: true));
        Assert.False((await db.UserFavorites.SingleAsync(f => f.Id == clubFav.Id)).IsFamily);
    }

    [Fact]
    public async Task SetFamily_FifthRefused_UnmarkAlwaysAllowed()
    {
        await using var db = CreateDb(nameof(SetFamily_FifthRefused_UnmarkAlwaysAllowed));
        var (owner, _, _, _) = await SeedAsync(db);
        var repo = Repo(db);
        var kids = Enumerable.Range(1, FavoritesRules.MaxFamily + 1)
            .Select(i => new Swimmer { LastName = $"Kid{i}", FirstName = "K", BirthYear = 2014 }).ToList();
        db.Swimmers.AddRange(kids);
        await db.SaveChangesAsync();
        var favs = new List<FavoriteDto>();
        foreach (var k in kids)
            favs.Add((await repo.AddAsync(owner.Id, new AddFavoriteRequest { TargetType = "swimmer", SwimmerId = k.Id })).Favorite!);

        var last = favs[favs.Count - 1];
        for (var i = 0; i < FavoritesRules.MaxFamily; i++)
            Assert.Equal(SetFamilyStatus.Done, await repo.SetFamilyAsync(owner.Id, favs[i].Id, isFamily: true));

        // Пятый — отказ, и пометка не встала.
        Assert.Equal(SetFamilyStatus.LimitReached, await repo.SetFamilyAsync(owner.Id, last.Id, isFamily: true));
        Assert.False((await db.UserFavorites.SingleAsync(f => f.Id == last.Id)).IsFamily);
        // Повтор уже поставленной пометки на пределе — не отказ.
        Assert.Equal(SetFamilyStatus.Done, await repo.SetFamilyAsync(owner.Id, favs[0].Id, isFamily: true));

        // Сняли одного — место появилось.
        Assert.Equal(SetFamilyStatus.Done, await repo.SetFamilyAsync(owner.Id, favs[0].Id, isFamily: false));
        Assert.Equal(SetFamilyStatus.Done, await repo.SetFamilyAsync(owner.Id, last.Id, isFamily: true));
    }

    // ── Уровни Me / Family / Favorite исключают друг друга (My favorites 1b, 24.09.2026) ──

    [Fact]
    public async Task SetPrimary_FromFamily_LeavesFamily()
    {
        await using var db = CreateDb(nameof(SetPrimary_FromFamily_LeavesFamily));
        var (owner, _, fav, _) = await SeedAsync(db);
        var repo = Repo(db);
        await repo.SetFamilyAsync(owner.Id, fav.Id, isFamily: true);

        Assert.True(await repo.SetPrimaryAsync(owner.Id, fav.Id));

        var row = await db.UserFavorites.AsNoTracking().SingleAsync(f => f.Id == fav.Id);
        Assert.True(row.IsPrimary);
        Assert.False(row.IsFamily);
    }

    [Fact]
    public async Task SetFamily_OnMe_ClearsMe()
    {
        await using var db = CreateDb(nameof(SetFamily_OnMe_ClearsMe));
        var (owner, _, fav, _) = await SeedAsync(db);
        var repo = Repo(db);
        await repo.SetPrimaryAsync(owner.Id, fav.Id);

        Assert.Equal(SetFamilyStatus.Done, await repo.SetFamilyAsync(owner.Id, fav.Id, isFamily: true));

        var row = await db.UserFavorites.AsNoTracking().SingleAsync(f => f.Id == fav.Id);
        Assert.False(row.IsPrimary);
        Assert.True(row.IsFamily);
    }

    [Fact]
    public async Task FamilyLimit_DoesNotCountLegacyMeWithFamilyMark()
    {
        // До 24.09.2026 «Me» мог нести и пометку семьи. Такая запись — уровень Me, и место в
        // семье она не занимает: иначе на пределе нельзя было бы пометить четвёртого.
        await using var db = CreateDb(nameof(FamilyLimit_DoesNotCountLegacyMeWithFamilyMark));
        var (owner, _, me, _) = await SeedAsync(db);
        var repo = Repo(db);
        var legacy = await db.UserFavorites.SingleAsync(f => f.Id == me.Id);
        legacy.IsPrimary = true;
        legacy.IsFamily = true;
        await db.SaveChangesAsync();

        var kids = Enumerable.Range(1, FavoritesRules.MaxFamily)
            .Select(i => new Swimmer { LastName = $"Kid{i}", FirstName = "K", BirthYear = 2014 }).ToList();
        db.Swimmers.AddRange(kids);
        await db.SaveChangesAsync();
        foreach (var k in kids)
        {
            var f = (await repo.AddAsync(owner.Id, new AddFavoriteRequest { TargetType = "swimmer", SwimmerId = k.Id })).Favorite!;
            Assert.Equal(SetFamilyStatus.Done, await repo.SetFamilyAsync(owner.Id, f.Id, isFamily: true));
        }
    }

    [Fact]
    public async Task GetForUser_SwimmerFavorite_CarriesSwimmersClub()
    {
        await using var db = CreateDb(nameof(GetForUser_SwimmerFavorite_CarriesSwimmersClub));
        var club = new Club { Name = "מכבי", NameEn = "Maccabi" };
        db.Clubs.Add(club);
        await db.SaveChangesAsync();
        var (owner, _, fav, clubFav) = await SeedAsync(db);
        var swimmer = await db.Swimmers.SingleAsync(s => s.Id == fav.SwimmerId);
        swimmer.ClubId = club.Id;
        await db.SaveChangesAsync();

        var list = await Repo(db).GetForUserAsync(owner.Id);

        var row = list.Single(f => f.Id == fav.Id);
        Assert.Equal(club.Id, row.SwimmerClubId);
        Assert.Equal("מכבי", row.SwimmerClubName); // исходное имя — по нему ищется эмблема
        Assert.Null(list.Single(f => f.Id == clubFav.Id).SwimmerClubName);
    }
}
