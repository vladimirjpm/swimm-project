using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="SeasonBestRepository"/> — национальный season best для таба рядом с
/// возрастными рекордами (design_handoff_age_records_sb).
///
/// Фиксируем ровно те решения, которые легко потерять при правке: ось возраста СЕЗОННАЯ,
/// masters не участвуют, мусорные заплывы (эстафетные ноги, TimeFail, SuspectReason) отброшены.
/// </summary>
public class SeasonBestRepositoryTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static SeasonBestRepository Repo(SwimmReadDbContext db) => new(db);

    private static Style FreestyleStyle() => new() { Id = 100, Name = "freestyle" };

    private static Competition MakeCompetition(
        string poolType = "25m", string date = "15/02/2026", bool isMasters = false) =>
        new() { Name = "Meet " + date + poolType, Date = date, PoolType = poolType, IsMasters = isMasters };

    private static Swimmer MakeSwimmer(Club club, string lastNameEn, int birthYear) => new()
    {
        Club = club,
        LastName = lastNameEn, FirstName = "X",
        LastNameEn = lastNameEn, FirstNameEn = "X",
        BirthYear = birthYear,
    };

    private static ResultRecord Swim(
        Competition comp, Club club, Swimmer swimmer, Style style, DateTime date,
        int timeMs = 60_000, string distance = "50", string gender = "male",
        bool timeFail = false, int? relayId = null, string? suspectReason = null) => new()
    {
        Competition = comp,
        Club = club,
        Swimmer = swimmer,
        Style = style,
        RelayId = relayId,
        CompetitionDate = date,
        Distance = distance,
        Gender = gender,
        TimeFail = timeFail,
        TimeMillisecond = timeFail ? null : timeMs,
        TimeOriginal = timeFail ? "" : $"{timeMs / 1000.0:0.00}",
        SuspectReason = suspectReason,
        InternationalPoints = 700,
    };

    /// <summary>Сезон 2025/26: 01/09/2025 — 31/08/2026, возраст = 2026 − год рождения.</summary>
    private const int Season = 2025;

    [Fact]
    public async Task PicksFastestPerGenderAndSeasonAge()
    {
        using var db = CreateDb(nameof(PicksFastestPerGenderAndSeasonAge));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var comp = MakeCompetition();
        var fast = MakeSwimmer(club, "Fast", 2016);   // в сезоне 2025/26 — 10 лет
        var slow = MakeSwimmer(club, "Slow", 2016);
        var older = MakeSwimmer(club, "Older", 2015); // 11 лет — своя ступень
        db.AddRange(club, comp, style, fast, slow, older);
        db.Add(Swim(comp, club, fast, style, new DateTime(2026, 2, 15), timeMs: 30_000));
        db.Add(Swim(comp, club, slow, style, new DateTime(2026, 2, 15), timeMs: 35_000));
        db.Add(Swim(comp, club, older, style, new DateTime(2026, 2, 15), timeMs: 33_000));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetNationalSeasonBestAsync("freestyle", "50", null, Season);

        Assert.Equal("2025/26", res.SeasonLabel);
        Assert.Equal(2, res.Data.Count);
        var age10 = Assert.Single(res.Data, i => i.Age == 10);
        Assert.Equal("Fast X", age10.Name);
        Assert.Equal(30_000, age10.TimeMs);
        Assert.Equal("15/02/2026", age10.Date);
        Assert.Contains(res.Data, i => i.Age == 11 && i.Name == "Older X");
    }

    [Fact]
    public async Task AgeIsSeasonWide_NotDateOfSwim()
    {
        // Осенний и весенний старты одного сезона обязаны дать ОДНУ ступень: возраст в
        // сезоне считается по году окончания (2026 − 2016 = 10), а не по дате заплыва.
        using var db = CreateDb(nameof(AgeIsSeasonWide_NotDateOfSwim));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var autumn = MakeCompetition(date: "10/10/2025");
        var spring = MakeCompetition(date: "15/03/2026");
        var swimmer = MakeSwimmer(club, "Same", 2016);
        db.AddRange(club, autumn, spring, style, swimmer);
        db.Add(Swim(autumn, club, swimmer, style, new DateTime(2025, 10, 10), timeMs: 34_000));
        db.Add(Swim(spring, club, swimmer, style, new DateTime(2026, 3, 15), timeMs: 32_000));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetNationalSeasonBestAsync("freestyle", "50", null, Season);

        var only = Assert.Single(res.Data);
        Assert.Equal(10, only.Age);
        Assert.Equal(32_000, only.TimeMs); // лучшее из двух стартов сезона
        Assert.Equal(2, res.Meets);
    }

    [Fact]
    public async Task ExcludesMastersCompetitions()
    {
        using var db = CreateDb(nameof(ExcludesMastersCompetitions));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var regular = MakeCompetition(date: "15/02/2026");
        var masters = MakeCompetition(date: "20/02/2026", isMasters: true);
        var kid = MakeSwimmer(club, "Kid", 2016);
        var adult = MakeSwimmer(club, "Adult", 1985);
        db.AddRange(club, regular, masters, style, kid, adult);
        db.Add(Swim(regular, club, kid, style, new DateTime(2026, 2, 15), timeMs: 34_000));
        db.Add(Swim(masters, club, adult, style, new DateTime(2026, 2, 20), timeMs: 28_000));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetNationalSeasonBestAsync("freestyle", "50", null, Season);

        var only = Assert.Single(res.Data);
        Assert.Equal("Kid X", only.Name);
        Assert.Equal(1, res.Meets); // мастерский старт не вошёл и в счётчик соревнований
    }

    [Fact]
    public async Task SkipsRelayLegsFailedAndSuspectSwims()
    {
        using var db = CreateDb(nameof(SkipsRelayLegsFailedAndSuspectSwims));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var comp = MakeCompetition();
        var a = MakeSwimmer(club, "Clean", 2016);
        var b = MakeSwimmer(club, "Relay", 2016);
        var c = MakeSwimmer(club, "Suspect", 2016);
        var d = MakeSwimmer(club, "Failed", 2016);
        db.AddRange(club, comp, style, a, b, c, d);
        db.Add(Swim(comp, club, a, style, new DateTime(2026, 2, 15), timeMs: 34_000));
        db.Add(Swim(comp, club, b, style, new DateTime(2026, 2, 15), timeMs: 25_000, relayId: 7));
        db.Add(Swim(comp, club, c, style, new DateTime(2026, 2, 15), timeMs: 26_000, suspectReason: "personal_outlier"));
        db.Add(Swim(comp, club, d, style, new DateTime(2026, 2, 15), timeFail: true));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetNationalSeasonBestAsync("freestyle", "50", null, Season);

        var only = Assert.Single(res.Data);
        Assert.Equal("Clean X", only.Name);
    }

    [Fact]
    public async Task TieGoesToEarlierSwim()
    {
        using var db = CreateDb(nameof(TieGoesToEarlierSwim));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var early = MakeCompetition(date: "10/10/2025");
        var late = MakeCompetition(date: "15/03/2026");
        var first = MakeSwimmer(club, "First", 2016);
        var second = MakeSwimmer(club, "Second", 2016);
        db.AddRange(club, early, late, style, first, second);
        db.Add(Swim(early, club, first, style, new DateTime(2025, 10, 10), timeMs: 33_000));
        db.Add(Swim(late, club, second, style, new DateTime(2026, 3, 15), timeMs: 33_000));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetNationalSeasonBestAsync("freestyle", "50", null, Season);

        Assert.Equal("First X", Assert.Single(res.Data).Name);
    }

    [Fact]
    public async Task FiltersByPoolAndDistanceAndSeason()
    {
        using var db = CreateDb(nameof(FiltersByPoolAndDistanceAndSeason));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var shortPool = MakeCompetition("25m", "15/02/2026");
        var longPool = MakeCompetition("50m", "20/02/2026");
        var prevSeason = MakeCompetition("25m", "15/02/2025");
        var s1 = MakeSwimmer(club, "Short", 2016);
        var s2 = MakeSwimmer(club, "Long", 2016);
        var s3 = MakeSwimmer(club, "Hundred", 2016);
        var s4 = MakeSwimmer(club, "LastYear", 2016);
        db.AddRange(club, shortPool, longPool, prevSeason, style, s1, s2, s3, s4);
        db.Add(Swim(shortPool, club, s1, style, new DateTime(2026, 2, 15), timeMs: 34_000));
        db.Add(Swim(longPool, club, s2, style, new DateTime(2026, 2, 20), timeMs: 31_000));
        db.Add(Swim(shortPool, club, s3, style, new DateTime(2026, 2, 15), timeMs: 30_000, distance: "100"));
        db.Add(Swim(prevSeason, club, s4, style, new DateTime(2025, 2, 15), timeMs: 29_000));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetNationalSeasonBestAsync("freestyle", "50", "25m", Season);

        var only = Assert.Single(res.Data);
        Assert.Equal("Short X", only.Name); // 50m-бассейн, 100м и прошлый сезон отфильтрованы
        Assert.Equal("25m", res.PoolType);
    }

    [Fact]
    public async Task SkipsSwimmersWithoutBirthYear()
    {
        // Без года рождения ступени нет — в отличие от клубной карточки, у таблицы по
        // возрастам корзины «n/a» не предусмотрено.
        using var db = CreateDb(nameof(SkipsSwimmersWithoutBirthYear));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var comp = MakeCompetition();
        var unknown = MakeSwimmer(club, "Unknown", 0);
        db.AddRange(club, comp, style, unknown);
        db.Add(Swim(comp, club, unknown, style, new DateTime(2026, 2, 15), timeMs: 25_000));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetNationalSeasonBestAsync("freestyle", "50", null, Season);

        Assert.Empty(res.Data);
    }

    [Fact]
    public async Task NormalizesShortGenderSpelling()
    {
        // Results.Gender живёт в двух написаниях; «M» и «male» — один и тот же пол,
        // иначе витрина разложила бы их по двум колонкам.
        using var db = CreateDb(nameof(NormalizesShortGenderSpelling));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var comp = MakeCompetition();
        var a = MakeSwimmer(club, "Word", 2016);
        var b = MakeSwimmer(club, "Letter", 2016);
        db.AddRange(club, comp, style, a, b);
        db.Add(Swim(comp, club, a, style, new DateTime(2026, 2, 15), timeMs: 34_000, gender: "male"));
        db.Add(Swim(comp, club, b, style, new DateTime(2026, 2, 15), timeMs: 33_000, gender: "M"));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetNationalSeasonBestAsync("freestyle", "50", null, Season);

        var only = Assert.Single(res.Data);
        Assert.Equal("male", only.Gender);
        Assert.Equal("Letter X", only.Name);
    }
}
