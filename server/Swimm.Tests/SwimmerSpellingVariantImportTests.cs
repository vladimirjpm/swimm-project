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
/// Импорт узнаёт пловца, записанного другим написанием (כתיב מלא/חסר, дефис в двойной фамилии) —
/// Р79, docs/data-integrity.md. Эталон: «יוקליס עמליה» из протоколов Эйлата — это #61745
/// «יוקלס עמליה» (SWIM TLV, 2011); каждый переимпорт заводил дубль, склеивали 06.10 и 07.10.
/// Условие — тот же год, тот же клуб и единственный кандидат.
/// </summary>
public class SwimmerSpellingVariantImportTests
{
    [Theory]
    [InlineData("יוקליס", "עמליה", "יוקלס", "עמליה")]
    [InlineData("בן-בסט", "יובל", "בן בסט", "יובל")]
    [InlineData("כוהן", "שירה", "כהן", "שירה")]
    [InlineData("קליטצ׳וק", "ליאורה", "קליטצ'וק", "ליאורה")]
    public void SpellingKey_SameForVariants(string lastA, string firstA, string lastB, string firstB) =>
        Assert.Equal(
            JsonImportService.SwimmerSpellingKey(lastA, firstA, 2011),
            JsonImportService.SwimmerSpellingKey(lastB, firstB, 2011));

    [Fact]
    public void SpellingKey_DiffersByYear_AndIsNullForLatin()
    {
        Assert.NotEqual(
            JsonImportService.SwimmerSpellingKey("יוקליס", "עמליה", 2011),
            JsonImportService.SwimmerSpellingKey("יוקלס", "עמליה", 2012));
        Assert.Null(JsonImportService.SwimmerSpellingKey("Ukeles", "Amalia", 2011));
        Assert.Null(JsonImportService.SwimmerSpellingKey("יוקלס", "עמליה", 0));
    }

    // ── Импорт ────────────────────────────────────────────────────────────────

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

    private static object Item(string last, string first, string club, string time = "0:24:00.00") => new
    {
        country = "ISR",
        competition = "Eilat open water",
        date = "06/03/2025",
        event_style_name = "Freestyle",
        event_style_len = "1600",
        event_style_gender = "female",
        pool_type = "25m",
        position = 1,
        heat = 1,
        lane = 1,
        last_name = last,
        first_name = first,
        birth_year = 2011,
        club,
        time
    };

    private static Stream ToStream(object payload) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    /// <summary>Карточка «יוקלס עמליה» 2011 с клубом SWIM TLV — как #61745 в живой базе.</summary>
    private static async Task<(SwimmDbContext Db, Swimmer Ukeles)> SeedAsync(string name)
    {
        var db = CreateDb(name);
        var club = new Club { Name = "SWIM TLV", NameEn = "" };
        db.Add(club);
        await db.SaveChangesAsync();
        var ukeles = new Swimmer { LastName = "יוקלס", FirstName = "עמליה", BirthYear = 2011, Gender = "female", ClubId = club.Id };
        db.Add(ukeles);
        await db.SaveChangesAsync();
        return (db, ukeles);
    }

    [Fact]
    public async Task Variant_SameYearAndClub_GoesToExistingCard()
    {
        var (db, ukeles) = await SeedAsync(nameof(Variant_SameYearAndClub_GoesToExistingCard));
        await using var _ = db;

        var result = await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("יוקליס", "עמליה", "SWIM TLV") }));

        Assert.Empty(result.ErrorMessages);
        Assert.Single(await db.Swimmers.ToListAsync());
        Assert.Equal(ukeles.Id, (await db.Results.SingleAsync()).SwimmerId);
        Assert.Contains(result.DiagnosticLog, l => l.StartsWith("Другое написание"));
    }

    [Fact]
    public async Task Variant_OtherClub_IsNewCard()
    {
        // Другой клуб — уже не уверенность: возможно, другой человек. Решает человек в Health.
        var (db, ukeles) = await SeedAsync(nameof(Variant_OtherClub_IsNewCard));
        await using var _ = db;

        await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("יוקליס", "עמליה", "מכבי חיפה") }));

        Assert.Equal(2, await db.Swimmers.CountAsync());
        Assert.NotEqual(ukeles.Id, (await db.Results.SingleAsync()).SwimmerId);
    }

    [Fact]
    public async Task Variant_TwoCandidates_IsNewCard()
    {
        // Двое подходят — гадать нельзя.
        var (db, ukeles) = await SeedAsync(nameof(Variant_TwoCandidates_IsNewCard));
        await using var _ = db;
        db.Add(new Swimmer { LastName = "יוקלס", FirstName = "עמלייה", BirthYear = 2011, ClubId = ukeles.ClubId });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item("יוקליס", "עמליה", "SWIM TLV") }));

        Assert.Equal(3, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task RelayLeg_Variant_GoesToExistingCard()
    {
        var (db, ukeles) = await SeedAsync(nameof(RelayLeg_Variant_GoesToExistingCard));
        await using var _ = db;

        var relay = new
        {
            country = "ISR",
            competition = "Eilat open water",
            date = "07/03/2025",
            event_style_name = "Freestyle",
            event_style_len = "4X1500",
            event_style_gender = "none",
            pool_type = "25m",
            position = 1,
            heat = 1,
            lane = 4,
            last_name = "יוקליס",
            first_name = "עמליה",
            birth_year = 2011,
            club = "SWIM TLV",
            time = "1:30:00.00",
            is_relay = true,
            relay_team_name = "SWIM TLV",
            relay_swimmers = new[]
            {
                new { order = 1, last_name = "יוקליס", first_name = "עמליה", birth_year = 2011 },
                new { order = 2, last_name = "לוי", first_name = "דנה", birth_year = 2010 },
            },
        };

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[] { relay }));

        Assert.Contains(await db.RelayMembers.ToListAsync(), m => m.SwimmerId == ukeles.Id);
        Assert.False(await db.Swimmers.AnyAsync(s => s.LastName == "יוקליס"));
    }
}
