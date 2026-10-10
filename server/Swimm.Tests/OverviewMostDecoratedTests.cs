using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain;
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

    private static ResultRecord Row(
        Fixture f, int swimmerId, int? position, int? relayId = null, string? heatType = null) => new()
    {
        CompetitionId = f.Comp.Id, SwimmerId = swimmerId, ClubId = f.Club.Id, StyleId = f.Style.Id,
        RelayId = relayId, Distance = "100", Gender = "male", CompetitionDate = Date,
        TimeOriginal = "1:00.00", Position = position, AgeGroup = "Open",
        EventStyleAge = "100 freestyle Open", HeatType = heatType
    };

    [Fact]
    public async Task PrelimPlace_IsNotAMedal()
    {
        // Бугрим: протокол печатает места и в предварительных — «первый в прелимах» не золото.
        // У חרותי иначе выходило 2 золота за одну дисциплину (prelim + финал в один день).
        await using var db = CreateDb(nameof(PrelimPlace_IsNotAMedal));
        var f = await SeedBaseAsync(db);

        var swimmer = NewSwimmer("Cheruti", "male");
        db.Add(swimmer);
        await db.SaveChangesAsync();

        db.Results.AddRange(
            Row(f, swimmer.Id, 1, heatType: "prelim"),
            Row(f, swimmer.Id, 1, heatType: "final"));
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = f.Comp.Id });

        var top = Assert.Single(overview.TopMedalists);
        Assert.Equal(1, top.Gold);
    }

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

    /// <summary>
    /// Общий финал «כללי» (Round = final-open) медали не даёт: зачётная единица — возрастная
    /// ступень, там пловец свою медаль и получает (Р43). То же со swim-off («extra»).
    /// </summary>
    [Theory]
    [InlineData(ResultRounds.FinalOpen, null)]
    [InlineData(null, "extra")]
    public async Task OpenFinalAndExtraHeat_AreNotMedals(string? round, string? heatType)
    {
        await using var db = CreateDb($"{nameof(OpenFinalAndExtraHeat_AreNotMedals)}-{round}-{heatType}");
        var f = await SeedBaseAsync(db);

        var swimmer = NewSwimmer("Open", "male");
        db.Add(swimmer);
        await db.SaveChangesAsync();

        var row = Row(f, swimmer.Id, 1, heatType: heatType);
        row.Round = round;
        db.Results.Add(row);
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = f.Comp.Id });

        Assert.Empty(overview.TopMedalists);
    }

    /// <summary>
    /// «Мокдамот и финал»: ступень разыграна утром (прямой финал) и вечером (финал) —
    /// медаль у ступени одна, по лучшему месту (И13). Клубных очков это не касается.
    /// </summary>
    [Fact]
    public async Task TwoRoundsOfOneAgeStep_GiveOneMedal_ByBestPlace()
    {
        await using var db = CreateDb(nameof(TwoRoundsOfOneAgeStep_GiveOneMedal_ByBestPlace));
        var f = await SeedBaseAsync(db);

        var swimmer = NewSwimmer("Twice", "male");
        db.Add(swimmer);
        await db.SaveChangesAsync();

        var morning = Row(f, swimmer.Id, 2);
        morning.Round = ResultRounds.TimedFinal;
        var evening = Row(f, swimmer.Id, 1);
        evening.Round = ResultRounds.Final;
        db.Results.AddRange(morning, evening);
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = f.Comp.Id });

        var top = Assert.Single(overview.TopMedalists);
        Assert.Equal(1, top.Gold);
        Assert.Equal(0, top.Silver);
    }

    /// <summary>
    /// Тоггл «Combine All Results» берёт объединённое место дисциплины — но только у
    /// соревнования с <c>ShowCombineAllResults</c>; у остальных тоггл не действует.
    /// Протокольное золото A с объединённым 4-м местом медалью в этом режиме не остаётся.
    /// </summary>
    [Theory]
    [InlineData(true, false, "A")]   // соревнование умеет combine, тоггл выключен → протокол
    [InlineData(true, true, "B")]    // тоггл включён → объединённое место
    [InlineData(false, true, "A")]   // соревнование combine не умеет → тоггл игнорируется
    public async Task Combined_UsesCombinedPlace_OnlyWhereCompetitionShowsIt(
        bool showCombine, bool combined, string expectedTop)
    {
        await using var db = CreateDb($"{nameof(Combined_UsesCombinedPlace_OnlyWhereCompetitionShowsIt)}-{showCombine}-{combined}");
        var f = await SeedBaseAsync(db);
        f.Comp.ShowCombineAllResults = showCombine;

        var a = NewSwimmer("A", "male");
        var b = NewSwimmer("B", "male");
        db.AddRange(a, b);
        await db.SaveChangesAsync();

        var rowA = Row(f, a.Id, 1);
        rowA.CombinedPlace = 4;
        var rowB = Row(f, b.Id, 2);
        rowB.CombinedPlace = 1;
        db.Results.AddRange(rowA, rowB);
        await db.SaveChangesAsync();

        var overview = await new ResultRepository(db, new NullCache())
            .GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = f.Comp.Id, Combined = combined });

        var top = Assert.Single(overview.TopMedalists);
        Assert.Equal(expectedTop == "A" ? a.Id : b.Id, top.SwimmerId);
        Assert.Equal(1, top.Gold);
    }

    /// <summary>
    /// Объединённое место, лежащее на prelim-строке (лучший заплыв был утром), медаль даёт:
    /// prelim-исключение относится к протокольному месту сессии, а не к рейтингу дисциплины.
    /// </summary>
    [Fact]
    public async Task Combined_PrelimRowWithCombinedPlace_IsAMedal()
    {
        await using var db = CreateDb(nameof(Combined_PrelimRowWithCombinedPlace_IsAMedal));
        var f = await SeedBaseAsync(db);
        f.Comp.ShowCombineAllResults = true;

        var swimmer = NewSwimmer("Morning", "male");
        db.Add(swimmer);
        await db.SaveChangesAsync();

        var row = Row(f, swimmer.Id, 1, heatType: "prelim");
        row.CombinedPlace = 1;
        db.Results.Add(row);
        await db.SaveChangesAsync();

        var repo = new ResultRepository(db, new NullCache());
        var protocol = await repo.GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = f.Comp.Id });
        var combined = await repo.GetCompetitionOverviewAsync(new ResultFilter { CompetitionId = f.Comp.Id, Combined = true });

        Assert.Empty(protocol.TopMedalists);
        Assert.Equal(1, Assert.Single(combined.TopMedalists).Gold);
    }
}
