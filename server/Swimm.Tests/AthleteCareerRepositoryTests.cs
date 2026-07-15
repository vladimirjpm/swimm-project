using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Карьера атлета (GetAthleteCareerAsync): счёт Competitions по событиям, а не по дням —
/// многодневный CompetitionEvent считается ОДНИМ соревнованием. См. multiday-client-grouping.
/// </summary>
public class AthleteCareerRepositoryTests
{
    private static DbContextOptions<SwimmReadDbContext> BuildOptions(string name) =>
        new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static SwimmReadDbContext CreateDb(string name) =>
        new SwimmReadDbContext(BuildOptions(name));

    private sealed class NullCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static ICacheService NoCache() => new NullCacheService();

    private const string AthleteName = "Иван Иванов";

    private static Swimmer NewSwimmer() => new()
    {
        LastName = "Иванов", FirstName = "Иван",
        LastNameEn = "Ivanov", FirstNameEn = "Ivan",
        BirthYear = 2000
    };

    private static Competition NewCompetition(string name, string date, int? eventId = null) => new()
    {
        Name = name, Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
        Date = date, PoolType = "50m", EventId = eventId
    };

    private static ResultRecord NewResult(int compId, int swimmerId, int styleId, string date, int clubId = 0) => new()
    {
        CompetitionId = compId, SwimmerId = swimmerId, ClubId = clubId, StyleId = styleId,
        Distance = "100", Gender = "male",
        CompetitionDate = DateTime.Parse(FromDdMmYyyy(date)),
        TimeOriginal = "1:00.00", TimeMillisecond = 60000,
        AgeGroup = "Open", EventStyleAge = "100 freestyle Open"
    };

    // Competition.Date хранится как dd/MM/yyyy строка; CompetitionDate — реальный DateTime.
    private static string FromDdMmYyyy(string d)
    {
        var parts = d.Split('/');
        return $"{parts[2]}-{parts[1]}-{parts[0]}";
    }

    [Fact]
    public async Task Career_EventWithTwoDays_CountsAsOneCompetition()
    {
        await using var db = CreateDb(nameof(Career_EventWithTwoDays_CountsAsOneCompetition));
        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "TestClub", NameEn = "TestClub" };
        var evt = new CompetitionEvent { Name = "Maccabiah" };
        var swimmer = NewSwimmer();
        db.AddRange(style, club, evt, swimmer);
        await db.SaveChangesAsync();

        var day1 = NewCompetition("Maccabiah Day 1", "01/07/2026", evt.Id);
        var day2 = NewCompetition("Maccabiah Day 2", "02/07/2026", evt.Id);
        db.AddRange(day1, day2);
        await db.SaveChangesAsync();

        db.Results.Add(NewResult(day1.Id, swimmer.Id, style.Id, "01/07/2026"));
        db.Results.Add(NewResult(day2.Id, swimmer.Id, style.Id, "02/07/2026"));
        await db.SaveChangesAsync();

        var repo = new ResultRepository(db, NoCache());
        var career = await repo.GetAthleteCareerAsync(AthleteName);

        Assert.NotNull(career);
        Assert.Equal(1, career!.Competitions);
    }

    [Fact]
    public async Task Career_TwoDaysOfEventPlusOneStandalone_CountsAsTwoCompetitions()
    {
        await using var db = CreateDb(nameof(Career_TwoDaysOfEventPlusOneStandalone_CountsAsTwoCompetitions));
        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "TestClub", NameEn = "TestClub" };
        var evt = new CompetitionEvent { Name = "Maccabiah" };
        var swimmer = NewSwimmer();
        db.AddRange(style, club, evt, swimmer);
        await db.SaveChangesAsync();

        var day1 = NewCompetition("Maccabiah Day 1", "01/07/2026", evt.Id);
        var day2 = NewCompetition("Maccabiah Day 2", "02/07/2026", evt.Id);
        var single = NewCompetition("Winter Cup", "01/01/2026");
        db.AddRange(day1, day2, single);
        await db.SaveChangesAsync();

        db.Results.Add(NewResult(day1.Id, swimmer.Id, style.Id, "01/07/2026"));
        db.Results.Add(NewResult(day2.Id, swimmer.Id, style.Id, "02/07/2026"));
        db.Results.Add(NewResult(single.Id, swimmer.Id, style.Id, "01/01/2026"));
        await db.SaveChangesAsync();

        var repo = new ResultRepository(db, NoCache());
        var career = await repo.GetAthleteCareerAsync(AthleteName);

        Assert.NotNull(career);
        Assert.Equal(2, career!.Competitions);
    }

    [Fact]
    public async Task Career_RelayMedalInSameEventDay_DoesNotAddExtraCompetition()
    {
        await using var db = CreateDb(nameof(Career_RelayMedalInSameEventDay_DoesNotAddExtraCompetition));
        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "TestClub", NameEn = "TestClub" };
        var evt = new CompetitionEvent { Name = "Maccabiah" };
        var swimmer = NewSwimmer();
        var relay = new Relay { TeamName = "Team A", SwimmersName = "Иван Иванов, Пётр Петров" };
        db.AddRange(style, club, evt, swimmer, relay);
        await db.SaveChangesAsync();

        var day1 = NewCompetition("Maccabiah Day 1", "01/07/2026", evt.Id);
        var day2 = NewCompetition("Maccabiah Day 2", "02/07/2026", evt.Id);
        db.AddRange(day1, day2);
        await db.SaveChangesAsync();

        // Индивидуальный заплыв в дне 1 события.
        db.Results.Add(NewResult(day1.Id, swimmer.Id, style.Id, "01/07/2026"));
        // Эстафетная медаль в дне 2 ТОГО ЖЕ события — не должна добавить второе "соревнование".
        db.Results.Add(new ResultRecord
        {
            CompetitionId = day2.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = style.Id,
            RelayId = relay.Id, Distance = "4x100", Gender = "male",
            CompetitionDate = DateTime.Parse("2026-07-02"),
            TimeOriginal = "3:30.00", AgeGroup = "Open", EventStyleAge = "4x100 freestyle Open",
            Position = 1
        });
        await db.SaveChangesAsync();

        var repo = new ResultRepository(db, NoCache());
        var career = await repo.GetAthleteCareerAsync(AthleteName);

        Assert.NotNull(career);
        Assert.Equal(1, career!.Competitions);
        Assert.Equal(1, career.Gold); // эстафетная медаль засчитана в итог
    }

    [Fact]
    public async Task Career_StandaloneCompetitionsWithoutEventId_CountsEachSeparately()
    {
        await using var db = CreateDb(nameof(Career_StandaloneCompetitionsWithoutEventId_CountsEachSeparately));
        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "TestClub", NameEn = "TestClub" };
        var swimmer = NewSwimmer();
        db.AddRange(style, club, swimmer);
        await db.SaveChangesAsync();

        var comp1 = NewCompetition("Winter Cup", "01/01/2026");
        var comp2 = NewCompetition("Spring Cup", "01/04/2026");
        db.AddRange(comp1, comp2);
        await db.SaveChangesAsync();

        db.Results.Add(NewResult(comp1.Id, swimmer.Id, style.Id, "01/01/2026"));
        db.Results.Add(NewResult(comp2.Id, swimmer.Id, style.Id, "01/04/2026"));
        await db.SaveChangesAsync();

        var repo = new ResultRepository(db, NoCache());
        var career = await repo.GetAthleteCareerAsync(AthleteName);

        Assert.NotNull(career);
        Assert.Equal(2, career!.Competitions);
    }
}
