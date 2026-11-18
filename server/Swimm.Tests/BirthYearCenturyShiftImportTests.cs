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
/// Год рождения со сдвигом на сто лет (Р104, docs/data-integrity.md). Эталон: «ברנשטיין לאה» —
/// реестр федерации (loglig 112959) держит 2046 вместо 1946, протоколы 2025 года печатают 2046,
/// и импорт заводил ей двойника #64764 с возрастом −21 (склеен 18.11.2026).
/// Условие — невозможный год, то же точное имя, тот же клуб.
/// </summary>
public class BirthYearCenturyShiftImportTests
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

    private static object Item(int birthYear, string club, string age) => new
    {
        country = "ISR",
        competition = "Arena masters",
        date = "04/04/2025",
        event_style_name = "Butterfly",
        event_style_len = "50",
        event_style_gender = "female",
        event_style_age = age,
        pool_type = "50m",
        position = 1,
        heat = 9,
        lane = 2,
        last_name = "ברנשטיין",
        first_name = "לאה",
        birth_year = birthYear,
        club,
        time = "00:56.68"
    };

    private static Stream ToStream(object payload) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    private static async Task<(SwimmDbContext Db, Swimmer Leah)> SeedAsync(string name)
    {
        var db = CreateDb(name);
        var club = new Club { Name = "מכבי פ.מ. רעות" };
        db.Add(club);
        await db.SaveChangesAsync();
        var leah = new Swimmer { LastName = "ברנשטיין", FirstName = "לאה", BirthYear = 1946, Gender = "female", ClubId = club.Id };
        db.Add(leah);
        await db.SaveChangesAsync();
        return (db, leah);
    }

    [Fact]
    public async Task FutureYear_SameNameAndClub_GoesToExistingCard_AndAgeIsFixed()
    {
        var (db, leah) = await SeedAsync(nameof(FutureYear_SameNameAndClub_GoesToExistingCard_AndAgeIsFixed));
        await using var _ = db;

        var result = await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item(2046, "מכבי פ.מ. רעות", "-21") }));

        Assert.Empty(result.ErrorMessages);
        Assert.Single(await db.Swimmers.ToListAsync());
        var row = await db.Results.SingleAsync();
        Assert.Equal(leah.Id, row.SwimmerId);
        Assert.Equal("79", row.EventStyleAge);
        Assert.Contains(result.DiagnosticLog, l => l.StartsWith("Год со сдвигом на сто лет"));
    }

    [Fact]
    public async Task FutureYear_OtherClub_IsNewCard()
    {
        // Другой клуб — уже не уверенность: решает человек в Health.
        var (db, leah) = await SeedAsync(nameof(FutureYear_OtherClub_IsNewCard));
        await using var _ = db;

        await new JsonImportService(db, new NullCache())
            .ImportAsync(ToStream(new[] { Item(2046, "מכבי חיפה", "-21") }));

        Assert.Equal(2, await db.Swimmers.CountAsync());
        Assert.NotEqual(leah.Id, (await db.Results.SingleAsync()).SwimmerId);
    }

    [Fact]
    public async Task PlausibleYear_IsNotShifted()
    {
        // 2046 − 100 = 1946 работает только для НЕВОЗМОЖНОГО года. Обычный год (тёзка 2012
        // против карточки 1912 не бывает, но правило не должно гадать) — новая карточка.
        var (db, _) = await SeedAsync(nameof(PlausibleYear_IsNotShifted));
        await using var __ = db;
        db.Add(new Swimmer { LastName = "כהן", FirstName = "דנה", BirthYear = 1912, ClubId = (await db.Clubs.SingleAsync()).Id });
        await db.SaveChangesAsync();

        var item = new
        {
            country = "ISR", competition = "Meet", date = "04/04/2025", event_style_name = "Freestyle",
            event_style_len = "50", event_style_gender = "female", pool_type = "25m", position = 1, heat = 1, lane = 1,
            last_name = "כהן", first_name = "דנה", birth_year = 2012, club = "מכבי פ.מ. רעות", time = "00:40.00"
        };
        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[] { item }));

        Assert.Equal(3, await db.Swimmers.CountAsync());
    }
}
