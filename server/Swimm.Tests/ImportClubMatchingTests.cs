using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Матчинг клуба при импорте (docs/data-integrity.md, инцидент И-9).
///
/// Клуб искался по паре Name|NameEn. Ивритский протокол NameEn не приносит, а у канонического
/// клуба он заполнен двуязычным импортом — совпадения нет, и переимпорт плодил клуб-дубль:
/// 2026-08-03 так появилось 59 клубов, забравших 5141 результат.
/// </summary>
public class ImportClubMatchingTests
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

    private static object Item(string club, string? clubEn = null) => new
    {
        country = "ISR",
        competition = "Meet",
        date = "01/06/2026",
        event_style_name = "Freestyle",
        event_style_len = "50",
        event_style_gender = "male",
        pool_type = "25m",
        position = 1,
        heat = 1,
        lane = 1,
        last_name = "Cohen",
        first_name = "Tal",
        birth_year = 2012,
        club,
        club_en = clubEn,
        time = "00:30.00"
    };

    private static Stream ToStream(object payload) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    [Fact]
    public async Task HebrewOnlyProtocol_ReusesClubWithFilledNameEn()
    {
        await using var db = CreateDb(nameof(HebrewOnlyProtocol_ReusesClubWithFilledNameEn));
        db.Clubs.Add(new Club { Name = "בני הרצליה", NameEn = "Bnei Herzeliya" });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[] { Item("בני הרצליה") }));

        var club = Assert.Single(await db.Clubs.ToListAsync());
        Assert.Equal("Bnei Herzeliya", club.NameEn);
        Assert.Equal(club.Id, (await db.Results.SingleAsync()).ClubId);
    }

    [Fact]
    public async Task ProtocolWithEnglishName_FillsEmptyNameEn()
    {
        // Зеркальный случай: в БД клуб без английского имени, протокол его принёс.
        await using var db = CreateDb(nameof(ProtocolWithEnglishName_FillsEmptyNameEn));
        db.Clubs.Add(new Club { Name = "בני הרצליה", NameEn = "" });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("בני הרצליה", "Bnei Herzeliya") }));

        var club = Assert.Single(await db.Clubs.ToListAsync());
        Assert.Equal("Bnei Herzeliya", club.NameEn);
    }

    [Fact]
    public async Task MergedClub_NotReused_NewClubCreated()
    {
        // Склеенный клуб — надгробие: вешать на него новые результаты нельзя, иначе merge
        // молча откатится. Матч по имени обязан его игнорировать.
        await using var db = CreateDb(nameof(MergedClub_NotReused_NewClubCreated));
        var canon = new Club { Name = "Канон", NameEn = "Canon" };
        db.Clubs.Add(canon);
        await db.SaveChangesAsync();
        db.Clubs.Add(new Club { Name = "בני הרצליה", NameEn = "Bnei Herzeliya", MergedIntoId = canon.Id });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[] { Item("בני הרצליה") }));

        var result = await db.Results.SingleAsync();
        var used = await db.Clubs.FirstAsync(c => c.Id == result.ClubId);
        Assert.Null(used.MergedIntoId);
        Assert.NotEqual("Канон", used.Name);
    }

    [Fact]
    public async Task MergedClubWithEmptyNameEn_NotReused_EvenOnExactMatch()
    {
        // Инцидент И-13. Надгробие с ПУСТЫМ NameEn + ивритский протокол (NameEn тоже пустой)
        // = точное совпадение пары «Name|''». Фоллбек по имени склеенных отсекал, а точный
        // матч — нет, и merge молча откатывался: 61 надгробие снова набрало 9848 результатов.
        await using var db = CreateDb(nameof(MergedClubWithEmptyNameEn_NotReused_EvenOnExactMatch));
        var canon = new Club { Name = "הפועל דולפין נתניה", NameEn = "Hapoel Dolphine Netanya" };
        db.Clubs.Add(canon);
        await db.SaveChangesAsync();
        db.Clubs.Add(new Club { Name = "הפועל דולפין נתניה", NameEn = "", MergedIntoId = canon.Id });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("הפועל דולפין נתניה") }));

        // Результат обязан уехать в канон, а не в надгробие, и третий клуб не рождается.
        var result = await db.Results.SingleAsync();
        Assert.Equal(canon.Id, result.ClubId);
        Assert.Equal(2, await db.Clubs.CountAsync());
    }

    [Fact]
    public async Task DifferentName_StillCreatesOwnClub()
    {
        await using var db = CreateDb(nameof(DifferentName_StillCreatesOwnClub));
        db.Clubs.Add(new Club { Name = "בני הרצליה", NameEn = "Bnei Herzeliya" });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[] { Item("מכבי חיפה") }));

        Assert.Equal(2, await db.Clubs.CountAsync());
    }
}
