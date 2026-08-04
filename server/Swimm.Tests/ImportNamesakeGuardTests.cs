using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Страж тёзок на импорте (инцидент И-11, docs/data-integrity.md).
///
/// Ключ матчинга пловца — «фамилия|имя|годРождения», без клуба и без внешнего id, поэтому
/// два ребёнка-тёзки одного года рождения склеивались в одну запись ВСЕГДА. Живой случай:
/// «אדרי איתי» 2015 — за один день семь заплывов за два клуба и 50 вольным дважды.
///
/// Признак, что это разные люди: в ОДНОМ соревновании пловец не может выступать за два клуба.
/// </summary>
public class ImportNamesakeGuardTests
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

    private static object Row(string club, string style, string distance, string time,
        int lane, string date = "19/02/2026", string competition = "Meet") => new
    {
        country = "ISR",
        competition,
        date,
        event_style_name = style,
        event_style_len = distance,
        event_style_gender = "male",
        pool_type = "25m",
        position = lane,
        heat = 1,
        lane,
        last_name = "אדרי",
        first_name = "איתי",
        birth_year = 2015,
        club,
        time
    };

    private static Stream ToStream(object payload) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    [Fact]
    public async Task TwoClubsInOneCompetition_AreTreatedAsDifferentPeople()
    {
        await using var db = CreateDb(nameof(TwoClubsInOneCompetition_AreTreatedAsDifferentPeople));

        var result = await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[]
        {
            Row("הפועל עומר", "freestyle", "50", "00:36.90", 7),
            Row("מכבי ירושלים", "freestyle", "50", "00:39.50", 4),
            Row("מכבי חיפה", "breaststroke", "50", "00:48.88", 3),
        }), "meet.json");

        Assert.Empty(result.ErrorMessages);

        var swimmers = await db.Swimmers.ToListAsync();
        // Три клуба — три разных человека с одним именем; склейки больше нет.
        Assert.Equal(3, swimmers.Count);

        // И у каждого ровно один клуб в этом соревновании.
        var byClub = await db.Results
            .GroupBy(r => r.SwimmerId)
            .Select(g => new { g.Key, Clubs = g.Select(x => x.ClubId).Distinct().Count() })
            .ToListAsync();
        Assert.All(byClub, x => Assert.Equal(1, x.Clubs));

        Assert.Contains(result.DiagnosticLog, l => l.Contains("Тёзки"));
    }

    [Fact]
    public async Task SameClub_StaysOneSwimmer()
    {
        // Обычный случай: несколько заплывов одного ребёнка за свой клуб — одна запись.
        await using var db = CreateDb(nameof(SameClub_StaysOneSwimmer));

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[]
        {
            Row("הפועל עומר", "freestyle", "50", "00:36.90", 7),
            Row("הפועל עומר", "breaststroke", "50", "00:48.88", 3),
            Row("הפועל עומר", "backstroke", "100", "01:48.43", 1),
        }), "meet.json");

        Assert.Single(await db.Swimmers.ToListAsync());
    }

    [Fact]
    public async Task ClubChangeBetweenCompetitions_StaysOneSwimmer()
    {
        // Переход в другой клуб между стартами — норма, разрезать по нему нельзя.
        // Именно поэтому страж смотрит на клуб ВНУТРИ одного соревнования, а не вообще.
        await using var db = CreateDb(nameof(ClubChangeBetweenCompetitions_StaysOneSwimmer));

        await new JsonImportService(db, new NullCache()).ImportAsync(ToStream(new[]
        {
            Row("הפועל עומר", "freestyle", "50", "00:36.90", 7, date: "17/02/2025", competition: "Meet A"),
            Row("מכבי ירושלים", "freestyle", "50", "00:35.10", 7, date: "02/01/2026", competition: "Meet B"),
        }), "meet.json");

        Assert.Single(await db.Swimmers.ToListAsync());
    }
}
