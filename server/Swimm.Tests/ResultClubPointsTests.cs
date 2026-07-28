using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Э6: клубные очки приезжают в каждой строке /api/results — считает сервер по правилу
/// СОРЕВНОВАНИЯ, клиент их только показывает. Правила карточек и подбора —
/// docs/competition-overview-cards.md.
/// </summary>
public class ResultClubPointsTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
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

    private static readonly DateTime Date = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Правило #1 — шкала 30/28/26, действует с 2025; правило #2 — manual, шкала 40/34/30.</summary>
    private static async Task<(Competition Comp, Style Style, Club Club)> SeedAsync(
        SwimmReadDbContext db, int? boundRuleId = null, bool showCombine = false)
    {
        db.PointRulesClubs.AddRange(
            new PointRuleClubs
            {
                Id = 1, Version = "2025.01", Scope = "all", EffectiveFrom = new DateOnly(2025, 1, 1),
                RelayMultiplier = 2,
                Entries =
                [
                    new PointRuleClubsEntry { Place = 1, Points = 30 },
                    new PointRuleClubsEntry { Place = 2, Points = 28 },
                    new PointRuleClubsEntry { Place = 3, Points = 26 }
                ]
            },
            new PointRuleClubs
            {
                Id = 2, Version = "2026.01-manual", Scope = "all", EffectiveFrom = new DateOnly(2026, 1, 1),
                ManualOnly = true, RelayMultiplier = 2,
                Entries =
                [
                    new PointRuleClubsEntry { Place = 1, Points = 40 },
                    new PointRuleClubsEntry { Place = 2, Points = 34 }
                ]
            });

        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var comp = new Competition
        {
            Name = "Meet",
            Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/06/2026",
            PoolType = "50m",
            ShowCombineAllResults = showCombine,
            PointRuleClubsId = boundRuleId
        };
        db.AddRange(style, club, comp);
        await db.SaveChangesAsync();
        return (comp, style, club);
    }

    private static ResultRecord Row(
        Competition comp, Style style, Club club, int swimmerId, int? position,
        bool timeFail = false, int? relayId = null, int? combinedPlace = null) => new()
    {
        CompetitionId = comp.Id, SwimmerId = swimmerId, ClubId = club.Id, StyleId = style.Id,
        RelayId = relayId, Distance = "100", Gender = "male", CompetitionDate = Date,
        TimeOriginal = "1:00.00", TimeFail = timeFail, Position = position,
        CombinedPlace = combinedPlace, AgeGroup = "Open", EventStyleAge = "100 freestyle Open"
    };

    private static async Task<List<ResultDto>> PageAsync(SwimmReadDbContext db, Competition comp)
    {
        var (items, _, _) = await new ResultRepository(db, new NullCache())
            .GetPagedAsync(new ResultFilter { CompetitionId = comp.Id }, page: 1, pageSize: 100);
        return items;
    }

    [Fact]
    public async Task ClubPoints_ComeFromRuleBoundToCompetition_NotFromDate()
    {
        await using var db = CreateDb(nameof(ClubPoints_ComeFromRuleBoundToCompetition_NotFromDate));
        // Привязано manual-правило #2 — именно его и надо применить, хотя по дате
        // автоподбор выбрал бы #1 (manual в подборе не участвует).
        var (comp, style, club) = await SeedAsync(db, boundRuleId: 2);
        var swimmer = new Swimmer { LastName = "A", FirstName = "A", LastNameEn = "A", FirstNameEn = "A", BirthYear = 2005 };
        db.Add(swimmer);
        await db.SaveChangesAsync();
        db.Results.Add(Row(comp, style, club, swimmer.Id, 1));
        await db.SaveChangesAsync();

        var row = Assert.Single(await PageAsync(db, comp));
        Assert.Equal(40, row.ClubPoints);
    }

    [Fact]
    public async Task ClubPoints_FallBackToDateRule_WhenNotBound()
    {
        await using var db = CreateDb(nameof(ClubPoints_FallBackToDateRule_WhenNotBound));
        var (comp, style, club) = await SeedAsync(db);
        var swimmer = new Swimmer { LastName = "A", FirstName = "A", LastNameEn = "A", FirstNameEn = "A", BirthYear = 2005 };
        db.Add(swimmer);
        await db.SaveChangesAsync();
        db.Results.Add(Row(comp, style, club, swimmer.Id, 2));
        await db.SaveChangesAsync();

        var row = Assert.Single(await PageAsync(db, comp));
        Assert.Equal(28, row.ClubPoints); // правило #1: manual #2 в автоподбор не идёт
    }

    [Fact]
    public async Task ClubPoints_RelayDoubled_TimeFailZero()
    {
        await using var db = CreateDb(nameof(ClubPoints_RelayDoubled_TimeFailZero));
        var (comp, style, club) = await SeedAsync(db, boundRuleId: 1);
        var swimmer = new Swimmer { LastName = "A", FirstName = "A", LastNameEn = "A", FirstNameEn = "A", BirthYear = 2005 };
        var relay = new Relay { TeamName = "Alpha Relay", SwimmersName = "A" };
        db.AddRange(swimmer, relay);
        await db.SaveChangesAsync();
        db.Results.AddRange(
            Row(comp, style, club, swimmer.Id, 1, relayId: relay.Id),
            Row(comp, style, club, swimmer.Id, 1, timeFail: true));
        await db.SaveChangesAsync();

        var rows = await PageAsync(db, comp);
        Assert.Equal(60, rows.Single(r => r.IsRelay).ClubPoints);     // 30 × множитель 2
        Assert.Equal(0, rows.Single(r => r.TimeFail).ClubPoints);     // DSQ очков не даёт
    }

    [Fact]
    public async Task CombinedClubPoints_FollowCombinedPlace_NullWhenAbsent()
    {
        await using var db = CreateDb(nameof(CombinedClubPoints_FollowCombinedPlace_NullWhenAbsent));
        var (comp, style, club) = await SeedAsync(db, boundRuleId: 1, showCombine: true);
        var swimmer = new Swimmer { LastName = "A", FirstName = "A", LastNameEn = "A", FirstNameEn = "A", BirthYear = 2005 };
        db.Add(swimmer);
        await db.SaveChangesAsync();
        db.Results.AddRange(
            Row(comp, style, club, swimmer.Id, 1, combinedPlace: 3),  // в своём заплыве 1-й, в общем зачёте 3-й
            Row(comp, style, club, swimmer.Id, 2));                   // объединённого места нет
        await db.SaveChangesAsync();

        var rows = await PageAsync(db, comp);
        var withCombined = rows.Single(r => r.CombinedPlace == 3);
        Assert.Equal(30, withCombined.ClubPoints);          // по протокольному месту
        Assert.Equal(26, withCombined.CombinedClubPoints);  // по объединённому

        var withoutCombined = rows.Single(r => r.CombinedPlace == null);
        Assert.Equal(28, withoutCombined.ClubPoints);
        Assert.Null(withoutCombined.CombinedClubPoints);
    }

    [Fact]
    public async Task RuleId_IsNotSerializedToClient()
    {
        await using var db = CreateDb(nameof(RuleId_IsNotSerializedToClient));
        var (comp, style, club) = await SeedAsync(db, boundRuleId: 2);
        var swimmer = new Swimmer { LastName = "A", FirstName = "A", LastNameEn = "A", FirstNameEn = "A", BirthYear = 2005 };
        db.Add(swimmer);
        await db.SaveChangesAsync();
        db.Results.Add(Row(comp, style, club, swimmer.Id, 1));
        await db.SaveChangesAsync();

        var row = Assert.Single(await PageAsync(db, comp));
        var json = System.Text.Json.JsonSerializer.Serialize(row);

        Assert.Contains("\"club_points\":40", json);
        Assert.DoesNotContain("PointRuleClubsId", json);
    }
}
