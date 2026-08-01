using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="ClubStandingService"/> — материализация клубного зачёта.
/// Главное, что здесь закреплено: многодневное событие даёт ОДНУ строку на клуб
/// (а не по строке на день), псевдоклубы в зачёт не попадают, а пересчёт полностью
/// заменяет прежний зачёт.
/// </summary>
public class ClubStandingServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>Правило «место → очки»: 1-е 9, 2-е 7, 3-е 6, остальные 0; эстафета ×2.</summary>
    private static PointRuleClubs Rule() => new()
    {
        Version = "test.01",
        Scope = "all",
        EffectiveFrom = new DateOnly(2000, 1, 1),
        DefaultPoints = 0,
        MaxScoringPlace = 3,
        RelayMultiplier = 2,
        Entries =
        [
            new PointRuleClubsEntry { Place = 1, Points = 9 },
            new PointRuleClubsEntry { Place = 2, Points = 7 },
            new PointRuleClubsEntry { Place = 3, Points = 6 },
        ]
    };

    private sealed record Seeded(Competition Day1, Competition? Day2, Club ClubA, Club ClubB);

    private static async Task<Seeded> SeedAsync(SwimmDbContext db, bool multiDay = false)
    {
        var country = new Country { CountryCode = "ISR", CountryName = "Israel" };
        var clubA = new Club { Name = "Alpha", NameEn = "Alpha", Country = country };
        var clubB = new Club { Name = "Beta", NameEn = "Beta", Country = country };
        var style = new Style { Id = 100, Name = "freestyle" };
        db.AddRange(country, clubA, clubB, style);

        Competition day1;
        Competition? day2 = null;
        if (multiDay)
        {
            var ev = new CompetitionEvent { Name = "Champ" };
            db.Add(ev);
            await db.SaveChangesAsync();
            day1 = new Competition { Name = "Champ", Date = "15/02/2026", PoolType = "25m", EventId = ev.Id, DayNumber = 1 };
            day2 = new Competition { Name = "Champ", Date = "16/02/2026", PoolType = "25m", EventId = ev.Id, DayNumber = 2 };
            db.AddRange(day1, day2);
        }
        else
        {
            day1 = new Competition { Name = "Meet", Date = "15/02/2026", PoolType = "25m" };
            db.Add(day1);
        }

        db.Add(Rule());
        await db.SaveChangesAsync();
        return new Seeded(day1, day2, clubA, clubB);
    }

    private static ResultRecord Swim(
        Competition comp, Club club, int swimmerId, int? position, bool timeFail = false, int? relayId = null) =>
        new()
        {
            CompetitionId = comp.Id,
            ClubId = club.Id,
            SwimmerId = swimmerId,
            StyleId = 100,
            RelayId = relayId,
            CompetitionDate = new DateTime(2026, 2, 15),
            Distance = "100",
            Gender = "male",
            Position = position,
            TimeFail = timeFail,
            TimeMillisecond = timeFail ? null : 60_000
        };

    private static ClubStandingService Service(SwimmDbContext db) => new(db);

    [Fact]
    public async Task SingleCompetition_BuildsRanksAndMedals()
    {
        using var db = CreateDb(nameof(SingleCompetition_BuildsRanksAndMedals));
        var s = await SeedAsync(db);
        db.AddRange(
            Swim(s.Day1, s.ClubA, swimmerId: 1, position: 1),   // 9
            Swim(s.Day1, s.ClubA, swimmerId: 2, position: 3),   // 6
            Swim(s.Day1, s.ClubB, swimmerId: 3, position: 2));  // 7
        await db.SaveChangesAsync();

        await Service(db).RebuildForCompetitionAsync(s.Day1.Id);

        var table = await db.ClubCompetitionStandings.OrderBy(x => x.Rank).ToListAsync();
        Assert.Equal(2, table.Count);
        Assert.Equal(s.ClubA.Id, table[0].ClubId);
        Assert.Equal(15, table[0].Points);
        Assert.Equal(1, table[0].Gold);
        Assert.Equal(1, table[0].Bronze);
        Assert.Equal(2, table[0].SwimmerCount);
        Assert.Equal(s.ClubB.Id, table[1].ClubId);
        Assert.Equal(2, table[1].Rank);
    }

    [Fact]
    public async Task MultiDayEvent_ProducesOneRowPerClub_OnFirstDay()
    {
        using var db = CreateDb(nameof(MultiDayEvent_ProducesOneRowPerClub_OnFirstDay));
        var s = await SeedAsync(db, multiDay: true);
        db.AddRange(
            Swim(s.Day1, s.ClubA, swimmerId: 1, position: 1),   // 9
            Swim(s.Day2!, s.ClubA, swimmerId: 1, position: 2)); // 7 — тот же пловец, второй день
        await db.SaveChangesAsync();

        // Пересчёт запускается по ВТОРОМУ дню — область всё равно всё событие.
        await Service(db).RebuildForCompetitionAsync(s.Day2!.Id);

        var row = Assert.Single(await db.ClubCompetitionStandings.ToListAsync());
        Assert.Equal(s.Day1.Id, row.CompetitionId); // строка висит на первом дне события
        Assert.Equal(16, row.Points);               // очки обоих дней сложены
        Assert.Equal(1, row.SwimmerCount);          // один пловец, два заплыва
        Assert.Equal(2, row.SwimCount);
    }

    [Fact]
    public async Task PseudoClubs_AreExcluded()
    {
        using var db = CreateDb(nameof(PseudoClubs_AreExcluded));
        var s = await SeedAsync(db);
        var pseudo = new Club { Name = "Israel", NameEn = "Israel", IsPseudo = true };
        db.Add(pseudo);
        await db.SaveChangesAsync();
        db.AddRange(
            Swim(s.Day1, s.ClubA, swimmerId: 1, position: 2),
            Swim(s.Day1, pseudo, swimmerId: 2, position: 1));
        await db.SaveChangesAsync();

        await Service(db).RebuildForCompetitionAsync(s.Day1.Id);

        var row = Assert.Single(await db.ClubCompetitionStandings.ToListAsync());
        Assert.Equal(s.ClubA.Id, row.ClubId);
    }

    [Fact]
    public async Task DisqualifiedSwim_EarnsNoPoints_ButCountsAsSwim()
    {
        using var db = CreateDb(nameof(DisqualifiedSwim_EarnsNoPoints_ButCountsAsSwim));
        var s = await SeedAsync(db);
        db.AddRange(
            Swim(s.Day1, s.ClubA, swimmerId: 1, position: 1),
            Swim(s.Day1, s.ClubA, swimmerId: 2, position: 2, timeFail: true));
        await db.SaveChangesAsync();

        await Service(db).RebuildForCompetitionAsync(s.Day1.Id);

        var row = Assert.Single(await db.ClubCompetitionStandings.ToListAsync());
        Assert.Equal(9, row.Points);      // DSQ очков не принёс
        Assert.Equal(1, row.ScoringSwims);
        Assert.Equal(2, row.SwimCount);
    }

    [Fact]
    public async Task Relay_GetsRuleMultiplier()
    {
        using var db = CreateDb(nameof(Relay_GetsRuleMultiplier));
        var s = await SeedAsync(db);
        var relay = new Relay { TeamName = "Alpha A" };
        db.Add(relay);
        await db.SaveChangesAsync();
        db.Add(Swim(s.Day1, s.ClubA, swimmerId: 1, position: 1, relayId: relay.Id));
        await db.SaveChangesAsync();

        await Service(db).RebuildForCompetitionAsync(s.Day1.Id);

        // 9 очков за первое место × RelayMultiplier 2.
        Assert.Equal(18, Assert.Single(await db.ClubCompetitionStandings.ToListAsync()).Points);
    }

    [Fact]
    public async Task Rebuild_ReplacesPreviousStanding()
    {
        using var db = CreateDb(nameof(Rebuild_ReplacesPreviousStanding));
        var s = await SeedAsync(db);
        var swim = Swim(s.Day1, s.ClubA, swimmerId: 1, position: 1);
        db.Add(swim);
        await db.SaveChangesAsync();
        await Service(db).RebuildForCompetitionAsync(s.Day1.Id);

        // Место исправили на третье — зачёт обязан перестроиться, а не задвоиться.
        swim.Position = 3;
        await db.SaveChangesAsync();
        await Service(db).RebuildForCompetitionAsync(s.Day1.Id);

        var row = Assert.Single(await db.ClubCompetitionStandings.ToListAsync());
        Assert.Equal(6, row.Points);
        Assert.Equal(0, row.Gold);
        Assert.Equal(1, row.Bronze);
    }

    [Fact]
    public async Task RebuildForClub_TouchesCompetitionsWhereClubSwam()
    {
        using var db = CreateDb(nameof(RebuildForClub_TouchesCompetitionsWhereClubSwam));
        var s = await SeedAsync(db);
        db.Add(Swim(s.Day1, s.ClubA, swimmerId: 1, position: 1));
        await db.SaveChangesAsync();

        var rows = await Service(db).RebuildForClubAsync(s.ClubA.Id);

        Assert.Equal(1, rows);
        Assert.Single(await db.ClubCompetitionStandings.ToListAsync());
    }
}
