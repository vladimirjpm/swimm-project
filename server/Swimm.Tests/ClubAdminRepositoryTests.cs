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

    // ── Удаление пустого клуба (мусор парсера из фильтра «Без пловцов») ──

    [Fact]
    public async Task DeleteEmpty_RemovesClub_AndReturnsNameForAudit()
    {
        await using var db = CreateDb(nameof(DeleteEmpty_RemovesClub_AndReturnsNameForAudit));
        var club = new Club { Name = "Гринберг Хапоэль Дольфин" };
        db.Clubs.Add(club);
        await db.SaveChangesAsync();

        var res = await Repo(db).DeleteEmptyAsync(club.Id);

        Assert.True(res.Success);
        Assert.Equal("Гринберг Хапоэль Дольфин", res.Name);
        Assert.Empty(db.Clubs);
    }

    [Fact]
    public async Task DeleteEmpty_ClubWithResults_Fails()
    {
        await using var db = CreateDb(nameof(DeleteEmpty_ClubWithResults_Fails));
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

        var res = await Repo(db).DeleteEmptyAsync(club.Id);

        Assert.False(res.Success);
        Assert.Single(db.Clubs);
    }

    [Fact]
    public async Task DeleteEmpty_ClubWithSwimmers_Fails()
    {
        await using var db = CreateDb(nameof(DeleteEmpty_ClubWithSwimmers_Fails));
        var club = new Club { Name = "Клуб" };
        db.Clubs.Add(club);
        db.Swimmers.Add(new Swimmer { LastName = "A", FirstName = "X", Club = club });
        await db.SaveChangesAsync();

        var res = await Repo(db).DeleteEmptyAsync(club.Id);

        Assert.False(res.Success);
        Assert.Single(db.Clubs);
    }

    [Fact]
    public async Task DeleteEmpty_MergedClub_Fails()
    {
        // Склеенный клуб — надгробие: по нему резолвятся старые ссылки /clubs/{id}.
        await using var db = CreateDb(nameof(DeleteEmpty_MergedClub_Fails));
        var canon = new Club { Name = "Канон" };
        db.Clubs.Add(canon);
        await db.SaveChangesAsync();
        var dup = new Club { Name = "Дубль", MergedIntoId = canon.Id };
        db.Clubs.Add(dup);
        await db.SaveChangesAsync();

        var res = await Repo(db).DeleteEmptyAsync(dup.Id);

        Assert.False(res.Success);
        Assert.Equal(2, await db.Clubs.CountAsync());
    }

    [Fact]
    public async Task DeleteEmpty_MergeTargetClub_Fails()
    {
        // Приёмник склейки: удаление оборвало бы MergedIntoId дубля (в БД — FK RESTRICT).
        await using var db = CreateDb(nameof(DeleteEmpty_MergeTargetClub_Fails));
        var canon = new Club { Name = "Канон" };
        db.Clubs.Add(canon);
        await db.SaveChangesAsync();
        db.Clubs.Add(new Club { Name = "Дубль", MergedIntoId = canon.Id });
        await db.SaveChangesAsync();

        var res = await Repo(db).DeleteEmptyAsync(canon.Id);

        Assert.False(res.Success);
        Assert.Equal(2, await db.Clubs.CountAsync());
    }

    [Fact]
    public async Task DeleteEmpty_PseudoClub_Fails()
    {
        await using var db = CreateDb(nameof(DeleteEmpty_PseudoClub_Fails));
        var club = new Club { Name = "Brazil", IsPseudo = true };
        db.Clubs.Add(club);
        await db.SaveChangesAsync();

        var res = await Repo(db).DeleteEmptyAsync(club.Id);

        Assert.False(res.Success);
        Assert.Single(db.Clubs);
    }

    [Fact]
    public async Task DeleteEmpty_MissingClub_Fails()
    {
        await using var db = CreateDb(nameof(DeleteEmpty_MissingClub_Fails));
        var res = await Repo(db).DeleteEmptyAsync(999);
        Assert.False(res.Success);
    }

    // ── Пакетная чистка «Удалить все пустые» ──

    [Fact]
    public async Task DeleteAllEmpty_RemovesOnlyEmptyOnes()
    {
        await using var db = CreateDb(nameof(DeleteAllEmpty_RemovesOnlyEmptyOnes));
        var empty1 = new Club { Name = "Мусор 1" };
        var empty2 = new Club { Name = "Мусор 2" };
        var pseudo = new Club { Name = "Brazil", IsPseudo = true };
        var withResult = new Club { Name = "Живой" };
        var withSwimmer = new Club { Name = "С пловцом" };
        var canon = new Club { Name = "Канон" };
        var comp = new Competition { Name = "Meet", Date = "01/06/2026", PoolType = "25m" };
        var style = new Style { Name = "freestyle" };
        var sw = new Swimmer { LastName = "A", FirstName = "X" };
        db.AddRange(empty1, empty2, pseudo, withResult, withSwimmer, canon, comp, style, sw);
        db.Swimmers.Add(new Swimmer { LastName = "B", FirstName = "Y", Club = withSwimmer });
        db.Results.Add(new ResultRecord
        {
            Club = withResult, Swimmer = sw, Competition = comp, Style = style,
            Distance = "50", Gender = "male", CompetitionDate = new DateTime(2026, 6, 1)
        });
        await db.SaveChangesAsync();
        // Надгробие склейки: и сам дубль, и его приёмник должны уцелеть.
        db.Clubs.Add(new Club { Name = "Дубль", MergedIntoId = canon.Id });
        await db.SaveChangesAsync();

        var res = await Repo(db).DeleteAllEmptyAsync();

        Assert.Equal(2, res.Deleted.Count);
        Assert.Equal(new[] { "Мусор 1", "Мусор 2" }, res.Deleted.Select(d => d.Name).OrderBy(n => n));
        // Канон сам по себе пуст и попадает в список фильтра — но он приёмник склейки, пропускаем.
        Assert.Contains("приёмник", Assert.Single(res.Skipped));
        Assert.Equal(5, await db.Clubs.CountAsync());
    }

    [Fact]
    public async Task DeleteAllEmpty_SkipsFavoritedClub_WithReason()
    {
        // Клуб пуст, но лежит у кого-то в избранном — в БД это FK RESTRICT.
        await using var db = CreateDb(nameof(DeleteAllEmpty_SkipsFavoritedClub_WithReason));
        var loved = new Club { Name = "В избранном" };
        var junk = new Club { Name = "Мусор" };
        db.AddRange(loved, junk);
        await db.SaveChangesAsync();
        db.UserFavorites.Add(new UserFavorite { UserId = 1, TargetType = "club", ClubId = loved.Id });
        await db.SaveChangesAsync();

        var res = await Repo(db).DeleteAllEmptyAsync();

        Assert.Equal("Мусор", Assert.Single(res.Deleted).Name);
        Assert.Contains("избранном", Assert.Single(res.Skipped));
        Assert.Equal("В избранном", (await db.Clubs.SingleAsync()).Name);
    }

    [Fact]
    public async Task DeleteAllEmpty_NothingToDelete_IsNoOp()
    {
        await using var db = CreateDb(nameof(DeleteAllEmpty_NothingToDelete_IsNoOp));
        db.Clubs.Add(new Club { Name = "Brazil", IsPseudo = true });
        await db.SaveChangesAsync();

        var res = await Repo(db).DeleteAllEmptyAsync();

        Assert.Empty(res.Deleted);
        Assert.Empty(res.Skipped);
        Assert.Single(db.Clubs);
    }
}
