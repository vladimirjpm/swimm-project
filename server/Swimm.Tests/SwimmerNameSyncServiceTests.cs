using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// «Синхронизация языков» (Admin/Discovery): дозаполнение EN-имён и канонизация пловцов,
/// созданных из EN-протокола, по двуязычной паре PDF — без переимпорта результатов.
/// </summary>
public class SwimmerNameSyncServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static SwimmerNameSyncService Svc(SwimmDbContext db) =>
        new(db, NullLogger<SwimmerNameSyncService>.Instance);

    /// <summary>results-JSON формата ResultWrap с одним пловцом (HE + EN имена).</summary>
    private static string Json(string heLast, string heFirst, string enLast, string enFirst, int year, bool relay = false) =>
        $$"""
        {"results":[{"last_name":"{{heLast}}","first_name":"{{heFirst}}",
        "last_name_en":"{{enLast}}","first_name_en":"{{enFirst}}",
        "birth_year":{{year}},"is_relay":{{(relay ? "true" : "false")}}}]}
        """;

    [Fact]
    public async Task MatchedByHebrew_FillsEmptyEnNames()
    {
        await using var db = CreateDb(nameof(MatchedByHebrew_FillsEmptyEnNames));
        db.Swimmers.Add(new Swimmer { LastName = "כהן", FirstName = "נטע", BirthYear = 2010 });
        await db.SaveChangesAsync();

        var r = await Svc(db).SyncFromResultsJsonAsync(Json("כהן", "נטע", "Cohen", "Neta", 2010));

        Assert.Equal(1, r.EnNamesFilled);
        Assert.Equal(0, r.NotFound);
        var s = await db.Swimmers.SingleAsync();
        Assert.Equal("Cohen", s.LastNameEn);
        Assert.Equal("Neta", s.FirstNameEn);
        Assert.Equal("כהן", s.LastName); // основные поля не тронуты
    }

    [Fact]
    public async Task MatchedByHebrew_ExistingEnNames_NotOverwritten()
    {
        await using var db = CreateDb(nameof(MatchedByHebrew_ExistingEnNames_NotOverwritten));
        db.Swimmers.Add(new Swimmer
        {
            LastName = "כהן", FirstName = "נטע", BirthYear = 2010,
            LastNameEn = "Kohen", FirstNameEn = "Netta"
        });
        await db.SaveChangesAsync();

        var r = await Svc(db).SyncFromResultsJsonAsync(Json("כהן", "נטע", "Cohen", "Neta", 2010));

        Assert.Equal(1, r.AlreadyComplete);
        Assert.Equal(0, r.EnNamesFilled);
        Assert.Equal("Kohen", (await db.Swimmers.SingleAsync()).LastNameEn);
    }

    [Fact]
    public async Task EnCreatedSwimmer_Canonized_HebrewIntoMainFields()
    {
        // Maccabiah-кейс: пловец создан из EN-протокола — английское имя в основных полях.
        await using var db = CreateDb(nameof(EnCreatedSwimmer_Canonized_HebrewIntoMainFields));
        db.Swimmers.Add(new Swimmer { LastName = "Cohen", FirstName = "Neta", BirthYear = 2010 });
        await db.SaveChangesAsync();

        var r = await Svc(db).SyncFromResultsJsonAsync(Json("כהן", "נטע", "Cohen", "Neta", 2010));

        Assert.Equal(1, r.Canonized);
        var s = await db.Swimmers.SingleAsync();
        Assert.Equal("כהן", s.LastName);
        Assert.Equal("נטע", s.FirstName);
        Assert.Equal("Cohen", s.LastNameEn);
        Assert.Equal("Neta", s.FirstNameEn);
    }

    [Fact]
    public async Task NoMatch_CountedNotFound_NothingCreated()
    {
        await using var db = CreateDb(nameof(NoMatch_CountedNotFound_NothingCreated));
        db.Swimmers.Add(new Swimmer { LastName = "לוי", FirstName = "דן", BirthYear = 2008 });
        await db.SaveChangesAsync();

        var r = await Svc(db).SyncFromResultsJsonAsync(Json("כהן", "נטע", "Cohen", "Neta", 2010));

        Assert.Equal(1, r.NotFound);
        Assert.Equal(1, await db.Swimmers.CountAsync()); // синхронизация никого не создаёт
    }

    [Fact]
    public async Task RelaysAndSynthetic_Ignored()
    {
        await using var db = CreateDb(nameof(RelaysAndSynthetic_Ignored));
        db.Swimmers.Add(new Swimmer
        {
            LastName = "Cohen", FirstName = "Neta", BirthYear = 2010, SwimmerOrgId = "SYNTH-1"
        });
        await db.SaveChangesAsync();

        // Эстафета не считается пловцом протокола.
        var relay = await Svc(db).SyncFromResultsJsonAsync(Json("כהן", "נטע", "Cohen", "Neta", 2010, relay: true));
        Assert.Equal(0, relay.SwimmersInProtocol);

        // Синтетический пловец не матчится (иначе канонизация испортила бы синтетику).
        var synth = await Svc(db).SyncFromResultsJsonAsync(Json("כהן", "נטע", "Cohen", "Neta", 2010));
        Assert.Equal(1, synth.NotFound);
        Assert.Equal("Cohen", (await db.Swimmers.SingleAsync()).LastName);
    }

    [Fact]
    public async Task EnEqualsHebrewFallback_NoEnInfo_TreatedComplete()
    {
        // ParseBilingual фоллбечит EN←HE при пустой английской строке — это не EN-имя.
        await using var db = CreateDb(nameof(EnEqualsHebrewFallback_NoEnInfo_TreatedComplete));
        db.Swimmers.Add(new Swimmer { LastName = "כהן", FirstName = "נטע", BirthYear = 2010 });
        await db.SaveChangesAsync();

        var r = await Svc(db).SyncFromResultsJsonAsync(Json("כהן", "נטע", "כהן", "נטע", 2010));

        Assert.Equal(1, r.AlreadyComplete);
        Assert.Equal("", (await db.Swimmers.SingleAsync()).LastNameEn ?? "");
    }
}
