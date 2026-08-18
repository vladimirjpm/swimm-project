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

    // IsAward = true по умолчанию: медаль засчитывается только там, где её вручали, и почти
    // все тесты здесь про медали. Старт без наград (лига) заводится явным IsAward: false.
    private static Competition NewCompetition(
        string name, string date, int? eventId = null, bool isAward = true) => new()
    {
        Name = name, Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
        Date = date, PoolType = "50m", EventId = eventId, IsAward = isAward
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

    // ── Карьера по id (этап A1 страницы спортсмена) ──────────────────────────────
    // Именной путь остаётся алиасом для попапа, но правда — по идентичности: имена в базе
    // не уникальны, и у тёзок именной путь склеивает двух разных людей в одну карьеру.

    [Fact]
    public async Task CareerById_Namesakes_AreNotMerged()
    {
        await using var db = CreateDb(nameof(CareerById_Namesakes_AreNotMerged));
        var style = new Style { Name = "freestyle" };
        var one = NewSwimmer();
        var twin = NewSwimmer();          // полный тёзка, другой человек
        db.AddRange(style, one, twin);
        await db.SaveChangesAsync();

        var comp1 = NewCompetition("Winter Cup", "01/01/2026");
        var comp2 = NewCompetition("Spring Cup", "01/04/2026");
        db.AddRange(comp1, comp2);
        await db.SaveChangesAsync();

        db.Results.Add(NewResult(comp1.Id, one.Id, style.Id, "01/01/2026"));
        db.Results.Add(NewResult(comp2.Id, twin.Id, style.Id, "01/04/2026"));
        await db.SaveChangesAsync();

        var repo = new ResultRepository(db, NoCache());

        var byName = await repo.GetAthleteCareerAsync(AthleteName);
        var byId = await repo.GetAthleteCareerByIdAsync(one.Id);

        Assert.Equal(2, byName!.Races);   // именной путь склеил обоих — так было и остаётся
        Assert.Equal(1, byId!.Races);     // по id — только свои заплывы
        Assert.Equal(1, byId.Competitions);
    }

    [Fact]
    public async Task CareerById_RelayMedal_FoundThroughRelayMembers()
    {
        await using var db = CreateDb(nameof(CareerById_RelayMedal_FoundThroughRelayMembers));
        var style = new Style { Name = "freestyle" };
        var owner = NewSwimmer();                                     // «первая нога» строки
        var leg = new Swimmer { FirstName = "Пётр", LastName = "Петров", BirthYear = 2001 };
        var relay = new Relay { TeamName = "Team A", SwimmersName = "Иван Иванов, Пётр Петров" };
        db.AddRange(style, owner, leg, relay);
        await db.SaveChangesAsync();

        var comp = NewCompetition("Winter Cup", "01/01/2026");
        db.Add(comp);
        await db.SaveChangesAsync();

        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = owner.Id, ClubId = 0, StyleId = style.Id,
            RelayId = relay.Id, Distance = "4x100", Gender = "male",
            CompetitionDate = DateTime.Parse("2026-01-01"),
            TimeOriginal = "3:30.00", AgeGroup = "Open", EventStyleAge = "4x100 freestyle Open",
            Position = 1
        });
        db.Add(new RelayMember { RelayId = relay.Id, SwimmerId = leg.Id, LegOrder = 2 });
        await db.SaveChangesAsync();

        var repo = new ResultRepository(db, NoCache());
        var career = await repo.GetAthleteCareerByIdAsync(leg.Id);

        Assert.NotNull(career);
        Assert.Equal(1, career!.Gold);          // командная награда так же личная
        Assert.Equal(0, career.Races);          // но личным заплывом не считается
        Assert.Equal(1, career.Competitions);
    }

    [Fact]
    public async Task Career_PlaceAtMeetWithoutAwards_IsNotAMedal()
    {
        // Найдено 2026-08-13 сверкой страницы спортсмена с карточкой: первое место на лиге
        // («ליגה רבתי 3», IsAward = false) шло в золото карьеры, хотя медалей там не вручают
        // и таблица результатов медаль на такой строке не рисует.
        await using var db = CreateDb(nameof(Career_PlaceAtMeetWithoutAwards_IsNotAMedal));
        var style = new Style { Name = "freestyle" };
        var swimmer = NewSwimmer();
        db.AddRange(style, swimmer);
        await db.SaveChangesAsync();

        var league = NewCompetition("League 3", "10/12/2025", isAward: false);
        var champs = NewCompetition("Winter championship", "16/02/2026");
        db.AddRange(league, champs);
        await db.SaveChangesAsync();

        var atLeague = NewResult(league.Id, swimmer.Id, style.Id, "10/12/2025");
        atLeague.Position = 1;
        var atChamps = NewResult(champs.Id, swimmer.Id, style.Id, "16/02/2026");
        atChamps.Position = 1;
        db.Results.AddRange(atLeague, atChamps);
        await db.SaveChangesAsync();

        var repo = new ResultRepository(db, NoCache());
        var career = await repo.GetAthleteCareerByIdAsync(swimmer.Id);

        Assert.Equal(1, career!.Gold);              // только чемпионат
        Assert.Single(career.Medals);
        Assert.Equal(2, career.Races);              // сам заплыв из карьеры не исчезает
    }

    [Fact]
    public async Task CareerById_UnknownId_ReturnsNull()
    {
        await using var db = CreateDb(nameof(CareerById_UnknownId_ReturnsNull));
        var repo = new ResultRepository(db, NoCache());

        Assert.Null(await repo.GetAthleteCareerByIdAsync(999));
        Assert.Null(await repo.GetAthleteCareerByIdAsync(0));
        Assert.Null(await repo.GetAthleteCareerByIdAsync(-3));
    }
}
