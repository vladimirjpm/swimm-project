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
/// Карточка «Most decorated» (Overview → Champions): медали личные + эстафетные,
/// порядок золото → серебро → бронза, ничья отдаёт всех.
/// Правила карточки описаны в docs/competition-overview-cards.md.
/// </summary>
public class OverviewMostDecoratedTests
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

    private static readonly DateTime Date = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Fixture
    {
        public required Competition Comp { get; init; }
        public required Style Style { get; init; }
        public required Club Club { get; init; }
    }

    private static async Task<Fixture> SeedBaseAsync(SwimmReadDbContext db)
    {
        var style = new Style { Name = "freestyle" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var comp = new Competition
        {
            Name = "Meet",
            Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/01/2024",
            PoolType = "50m"
        };
        db.AddRange(style, club, comp);
        await db.SaveChangesAsync();
        return new Fixture { Comp = comp, Style = style, Club = club };
    }

    private static Swimmer NewSwimmer(string tag, string gender) => new()
    {
        LastName = tag, FirstName = tag, LastNameEn = tag, FirstNameEn = tag,
        BirthYear = 2000, Gender = gender
    };

    private static ResultRecord Row(Fixture f, int swimmerId, int? position, int? relayId = null) => new()
    {
        CompetitionId = f.Comp.Id, SwimmerId = swimmerId, ClubId = f.Club.Id, StyleId = f.Style.Id,
        RelayId = relayId, Distance = "100", Gender = "male", CompetitionDate = Date,
        TimeOriginal = "1:00.00", Position = position, AgeGroup = "Open",
        EventStyleAge = "100 freestyle Open"
    };

    [Fact]
    public async Task RelayMedal_GoesToEveryMember_NotRowOwner()
    {
        await using var db = CreateDb(nameof(RelayMedal_GoesToEveryMember_NotRowOwner));
        var f = await SeedBaseAsync(db);

        var anchor = NewSwimmer("Anchor", "male");   // владелец строки эстафеты
        var mate = NewSwimmer("Mate", "male");       // вторая нога — медаль должна быть и у него
        var relay = new Relay { TeamName = "Alpha Relay", SwimmersName = "Anchor, Mate" };
        db.AddRange(anchor, mate, relay);
        await db.SaveChangesAsync();

        db.RelayMembers.AddRange(
            new RelayMember { RelayId = relay.Id, SwimmerId = anchor.Id, LegOrder = 1 },
            new RelayMember { RelayId = relay.Id, SwimmerId = mate.Id, LegOrder = 2 });
        db.Results.Add(Row(f, anchor.Id, 1, relay.Id));
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = f.Comp.Id });

        // Оба участника — с одинаковым набором (1 золото), значит ничья и оба в списке.
        Assert.Equal(2, overview.TopMedalists.Count);
        Assert.Contains(overview.TopMedalists, m => m.SwimmerId == mate.Id);
        Assert.All(overview.TopMedalists, m =>
        {
            Assert.Equal(1, m.Gold);
            Assert.Equal(1, m.RelayMedals);
            Assert.True(m.IsTie);
        });
    }

    [Fact]
    public async Task PersonalAndRelayMedals_AreSummed()
    {
        await using var db = CreateDb(nameof(PersonalAndRelayMedals_AreSummed));
        var f = await SeedBaseAsync(db);

        var swimmer = NewSwimmer("Solo", "male");
        var relay = new Relay { TeamName = "Alpha Relay", SwimmersName = "Solo" };
        db.AddRange(swimmer, relay);
        await db.SaveChangesAsync();

        db.RelayMembers.Add(new RelayMember { RelayId = relay.Id, SwimmerId = swimmer.Id, LegOrder = 1 });
        db.Results.AddRange(
            Row(f, swimmer.Id, 1),                 // личное золото
            Row(f, swimmer.Id, 3),                 // личная бронза
            Row(f, swimmer.Id, 1, relay.Id));      // эстафетное золото
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = f.Comp.Id });

        var top = Assert.Single(overview.TopMedalists);
        Assert.Equal(2, top.Gold);
        Assert.Equal(0, top.Silver);
        Assert.Equal(1, top.Bronze);
        Assert.Equal(1, top.RelayMedals);
    }

    [Fact]
    public async Task Ordering_IsGoldThenSilverThenBronze_NotTotalCount()
    {
        await using var db = CreateDb(nameof(Ordering_IsGoldThenSilverThenBronze_NotTotalCount));
        var f = await SeedBaseAsync(db);

        var oneGold = NewSwimmer("Gold", "male");     // 1 золото
        var manyBronze = NewSwimmer("Bronze", "male"); // 5 бронз — медалей больше, но зачёт ниже
        db.AddRange(oneGold, manyBronze);
        await db.SaveChangesAsync();

        db.Results.Add(Row(f, oneGold.Id, 1));
        for (var i = 0; i < 5; i++) db.Results.Add(Row(f, manyBronze.Id, 3));
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = f.Comp.Id });

        var top = Assert.Single(overview.TopMedalists);
        Assert.Equal(oneGold.Id, top.SwimmerId);
        Assert.Equal(1, top.Gold);
    }

    [Fact]
    public async Task Tie_ReturnsAllWithIdenticalMedalSet()
    {
        await using var db = CreateDb(nameof(Tie_ReturnsAllWithIdenticalMedalSet));
        var f = await SeedBaseAsync(db);

        var a = NewSwimmer("Aaa", "male");
        var b = NewSwimmer("Bbb", "male");
        var c = NewSwimmer("Ccc", "male");  // тот же счёт медалей, но другой набор
        db.AddRange(a, b, c);
        await db.SaveChangesAsync();

        db.Results.AddRange(
            Row(f, a.Id, 1), Row(f, a.Id, 2),
            Row(f, b.Id, 1), Row(f, b.Id, 2),
            Row(f, c.Id, 1), Row(f, c.Id, 3));
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = f.Comp.Id });

        Assert.Equal(2, overview.TopMedalists.Count);
        Assert.All(overview.TopMedalists, m => Assert.True(m.IsTie));
        Assert.DoesNotContain(overview.TopMedalists, m => m.SwimmerId == c.Id);
    }

    [Fact]
    public async Task GenderSplit_UsesSwimmerGender_ForRelayLegs()
    {
        await using var db = CreateDb(nameof(GenderSplit_UsesSwimmerGender_ForRelayLegs));
        var f = await SeedBaseAsync(db);

        // Смешанная эстафета: строка результата помечена male, но одна нога — женщина.
        var man = NewSwimmer("Man", "male");
        var woman = NewSwimmer("Woman", "female");
        var relay = new Relay { TeamName = "Mixed Relay", SwimmersName = "Man, Woman" };
        db.AddRange(man, woman, relay);
        await db.SaveChangesAsync();

        db.RelayMembers.AddRange(
            new RelayMember { RelayId = relay.Id, SwimmerId = man.Id, LegOrder = 1 },
            new RelayMember { RelayId = relay.Id, SwimmerId = woman.Id, LegOrder = 2 });
        db.Results.Add(Row(f, man.Id, 1, relay.Id));
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = f.Comp.Id });

        Assert.Equal(man.Id, Assert.Single(overview.TopMedalistsMale).SwimmerId);
        Assert.Equal(woman.Id, Assert.Single(overview.TopMedalistsFemale).SwimmerId);
    }

    [Fact]
    public async Task TimeFail_GivesNoMedal()
    {
        await using var db = CreateDb(nameof(TimeFail_GivesNoMedal));
        var f = await SeedBaseAsync(db);

        var swimmer = NewSwimmer("Dsq", "male");
        db.Add(swimmer);
        await db.SaveChangesAsync();

        var row = Row(f, swimmer.Id, 1);
        row.TimeFail = true;
        db.Results.Add(row);
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = f.Comp.Id });

        Assert.Empty(overview.TopMedalists);
    }
}
