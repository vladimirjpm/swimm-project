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
/// Матчинг пловца в импорте по нормализованному ключу (см.
/// docs/tasks/import-name-normalize-sonnet.md): гереш/апостроф, регистр латиницы,
/// двойные пробелы не должны плодить дублей; реальные разные люди — не склеиваются.
/// </summary>
public class SwimmerImportMatchingTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>ICacheService-заглушка — импорту кэш не важен для этих тестов.</summary>
    private sealed class NullCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    /// <summary>Строит один результат в формате ResultJsonItem для импорта.</summary>
    private static object Item(string lastName, string firstName, int birthYear,
        string competition = "Comp", string date = "01/06/2026") => new
        {
            country = "ISR",
            competition,
            date,
            event_style_name = "Freestyle",
            event_style_len = "50",
            event_style_gender = "male",
            pool_type = "25m",
            position = 1,
            last_name = lastName,
            first_name = firstName,
            birth_year = birthYear,
            club = "Club",
            time = "00:30.00"
        };

    /// <summary>Строит анонимный relay-лег (без имени/фамилии/года рождения) для конкретной команды.</summary>
    private static object AnonymousRelayItem(string club, int position,
        string competition = "Comp", string date = "01/06/2026") => new
        {
            country = "ISR",
            competition,
            date,
            event_style_name = "Freestyle",
            event_style_len = "50",
            event_style_gender = "male",
            pool_type = "25m",
            position,
            last_name = "",
            first_name = "",
            birth_year = (int?)null,
            club,
            time = "00:30.00",
            is_relay = true,
            relay_team_name = club
        };

    private static Stream ToStream(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    [Fact]
    public async Task Import_GereshVsApostrophe_MatchesExistingSwimmer()
    {
        await using var db = CreateDb(nameof(Import_GereshVsApostrophe_MatchesExistingSwimmer));
        var existing = new Swimmer { LastName = "בויצ׳וק", FirstName = "ניר", BirthYear = 2010 };
        db.Swimmers.Add(existing);
        await db.SaveChangesAsync();

        var svc = new JsonImportService(db, new NullCacheService());
        var result = await svc.ImportAsync(ToStream(new[] { Item("בויצ'וק", "ניר", 2010) }));

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(1, await db.Swimmers.CountAsync());
        var res = await db.Results.SingleAsync();
        Assert.Equal(existing.Id, res.SwimmerId);
    }

    [Fact]
    public async Task Import_TypographicApostropheVsAscii_MatchesExistingSwimmer()
    {
        await using var db = CreateDb(nameof(Import_TypographicApostropheVsAscii_MatchesExistingSwimmer));
        var existing = new Swimmer { LastName = "O’Brien", FirstName = "Sam", BirthYear = 2011 };
        db.Swimmers.Add(existing);
        await db.SaveChangesAsync();

        var svc = new JsonImportService(db, new NullCacheService());
        var result = await svc.ImportAsync(ToStream(new[] { Item("O'Brien", "Sam", 2011) }));

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(1, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task Import_LatinCase_MatchesExistingSwimmer()
    {
        await using var db = CreateDb(nameof(Import_LatinCase_MatchesExistingSwimmer));
        var existing = new Swimmer { LastName = "HARRISON", FirstName = "Zaq", BirthYear = 2012 };
        db.Swimmers.Add(existing);
        await db.SaveChangesAsync();

        var svc = new JsonImportService(db, new NullCacheService());
        var result = await svc.ImportAsync(ToStream(new[] { Item("Harrison", "Zaq", 2012) }));

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(1, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task Import_DoubleSpace_MatchesExistingSwimmer()
    {
        await using var db = CreateDb(nameof(Import_DoubleSpace_MatchesExistingSwimmer));
        var existing = new Swimmer { LastName = "Van  Der Berg", FirstName = "Lee", BirthYear = 2013 };
        db.Swimmers.Add(existing);
        await db.SaveChangesAsync();

        var svc = new JsonImportService(db, new NullCacheService());
        var result = await svc.ImportAsync(ToStream(new[] { Item("Van Der Berg", "Lee", 2013) }));

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(1, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task Import_DifferentPeopleByLetter_NotMerged()
    {
        await using var db = CreateDb(nameof(Import_DifferentPeopleByLetter_NotMerged));
        var existing = new Swimmer { LastName = "דרזנר", FirstName = "דין", BirthYear = 2014 };
        db.Swimmers.Add(existing);
        await db.SaveChangesAsync();

        var svc = new JsonImportService(db, new NullCacheService());
        var result = await svc.ImportAsync(ToStream(new[] { Item("דרזנר", "שון", 2014) }));

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(2, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task Import_NameSurnameSwap_NotMerged()
    {
        await using var db = CreateDb(nameof(Import_NameSurnameSwap_NotMerged));
        var existing = new Swimmer { LastName = "שני", FirstName = "עידו", BirthYear = 2015 };
        db.Swimmers.Add(existing);
        await db.SaveChangesAsync();

        var svc = new JsonImportService(db, new NullCacheService());
        var result = await svc.ImportAsync(ToStream(new[] { Item("עידו", "שני", 2015) }));

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(2, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task Import_DifferentBirthYear_NotMerged()
    {
        await using var db = CreateDb(nameof(Import_DifferentBirthYear_NotMerged));
        var existing = new Swimmer { LastName = "Cohen", FirstName = "Tal", BirthYear = 2005 };
        db.Swimmers.Add(existing);
        await db.SaveChangesAsync();

        var svc = new JsonImportService(db, new NullCacheService());
        var result = await svc.ImportAsync(ToStream(new[] { Item("Cohen", "Tal", 2006) }));

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(2, await db.Swimmers.CountAsync());
    }

    /// <summary>
    /// Регрессия бага swimmer_id 8243: анонимные relay-леги (пустые имя/фамилия/год —
    /// напр. DQ/NS-леги) все нормализуются в один и тот же пустой ключ. Раньше это
    /// схлопывало не связанные между собой леги разных команд в одного Swimmer.
    /// </summary>
    [Fact]
    public async Task Import_TwoAnonymousRelayLegsDifferentTeams_NotMerged()
    {
        await using var db = CreateDb(nameof(Import_TwoAnonymousRelayLegsDifferentTeams_NotMerged));

        var svc = new JsonImportService(db, new NullCacheService());
        var result = await svc.ImportAsync(ToStream(new[]
        {
            AnonymousRelayItem("Israel", 1),
            AnonymousRelayItem("USA", 2)
        }));

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(2, await db.Swimmers.CountAsync());

        var results = await db.Results.ToListAsync();
        Assert.Equal(2, results.Count);
        Assert.NotEqual(results[0].SwimmerId, results[1].SwimmerId);
    }

    /// <summary>Anonymous placeholder rows must not merge with each other even across many imports/teams.</summary>
    [Fact]
    public async Task Import_MultipleAnonymousRelayLegs_EachGetsOwnSwimmer()
    {
        await using var db = CreateDb(nameof(Import_MultipleAnonymousRelayLegs_EachGetsOwnSwimmer));

        var svc = new JsonImportService(db, new NullCacheService());
        var result = await svc.ImportAsync(ToStream(new[]
        {
            AnonymousRelayItem("Israel", 1),
            AnonymousRelayItem("USA", 2),
            AnonymousRelayItem("Germany", 3),
            AnonymousRelayItem("Brazil", 4),
            AnonymousRelayItem("Maccabiah MIX", 5)
        }));

        Assert.Empty(result.ErrorMessages);
        Assert.Equal(5, await db.Swimmers.CountAsync());

        var swimmerIds = (await db.Results.Select(r => r.SwimmerId).ToListAsync()).Distinct().ToList();
        Assert.Equal(5, swimmerIds.Count);
    }

    /// <summary>Named swimmers must still dedup exactly as before — the anonymous-key bypass is scoped narrowly.</summary>
    [Fact]
    public async Task Import_NamedSwimmerStillDedups_WhenAnonymousLegsAlsoPresent()
    {
        await using var db = CreateDb(nameof(Import_NamedSwimmerStillDedups_WhenAnonymousLegsAlsoPresent));
        var existing = new Swimmer { LastName = "Cohen", FirstName = "Tal", BirthYear = 2005 };
        db.Swimmers.Add(existing);
        await db.SaveChangesAsync();

        var svc = new JsonImportService(db, new NullCacheService());
        var result = await svc.ImportAsync(ToStream(new object[]
        {
            Item("Cohen", "Tal", 2005),
            AnonymousRelayItem("Israel", 1),
            AnonymousRelayItem("USA", 2)
        }));

        Assert.Empty(result.ErrorMessages);
        // 1 matched named swimmer + 2 distinct anonymous placeholders = 3 total
        Assert.Equal(3, await db.Swimmers.CountAsync());
    }
}
