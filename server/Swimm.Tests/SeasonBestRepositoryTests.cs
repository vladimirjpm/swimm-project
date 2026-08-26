using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Dtos;
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
        string poolType = "25m", string date = "15/02/2026", bool isMasters = false,
        string? standingKindOverride = null) =>
        new()
        {
            Name = "Meet " + date + poolType + (standingKindOverride ?? ""),
            Date = date,
            PoolType = poolType,
            IsMasters = isMasters,
            StandingKindOverride = standingKindOverride,
        };

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

    // ── Список одной дисциплины (страница /season-best) ───────────────────────────────────

    private static SeasonBestListQuery ListQuery(
        int? age = null, string? gender = null, int? clubId = null,
        bool best = false, int limit = 50, int offset = 0,
        int? ageTo = null, bool masters = false, string? ageGroup = null) => new()
    {
        Style = "freestyle",
        Distance = "50",
        Season = Season,
        Age = age,
        AgeTo = ageTo,
        Gender = gender,
        ClubId = clubId,
        Masters = masters,
        AgeGroup = ageGroup,
        BestPerSwimmer = best,
        Limit = limit,
        Offset = offset,
    };

    [Fact]
    public async Task ListKeepsRepeatSwimsAndNumbersAttempts()
    {
        // Главное правило страницы: дедупа по пловцу НЕТ. Один человек законно занимает и
        // первое место, и третье — это его разные старты сезона, и витрина обязана их
        // различать по номеру попытки.
        using var db = CreateDb(nameof(ListKeepsRepeatSwimsAndNumbersAttempts));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var winter = MakeCompetition(date: "10/12/2025");
        var spring = MakeCompetition(date: "15/03/2026");
        var star = MakeSwimmer(club, "Star", 2016);
        var rival = MakeSwimmer(club, "Rival", 2016);
        db.AddRange(club, winter, spring, style, star, rival);
        db.Add(Swim(winter, club, star, style, new DateTime(2025, 12, 10), timeMs: 30_000));
        db.Add(Swim(spring, club, star, style, new DateTime(2026, 3, 15), timeMs: 32_000));
        db.Add(Swim(winter, club, star, style, new DateTime(2025, 12, 10), timeMs: 34_000));
        db.Add(Swim(spring, club, rival, style, new DateTime(2026, 3, 15), timeMs: 31_000));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetSeasonBestListAsync(ListQuery());

        Assert.Equal(4, res.Total);
        Assert.Equal(2, res.Swimmers);
        Assert.Equal([1, 2, 3, 4], res.Data.Select(i => i.Place));
        // Star стоит на 1, 3 и 4 местах — и это его 1-я, 2-я и 3-я попытки.
        Assert.Equal([1, 1, 2, 3], res.Data.Select(i => i.Attempt));
        Assert.Equal("Star X", res.Data[0].Name);
        Assert.Equal("Rival X", res.Data[1].Name);
        Assert.Equal(0, res.Data[0].GapMs);
        Assert.Equal(1_000, res.Data[1].GapMs);
    }

    [Fact]
    public async Task ListBestPerSwimmerCollapsesRepeats()
    {
        using var db = CreateDb(nameof(ListBestPerSwimmerCollapsesRepeats));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var comp = MakeCompetition();
        var star = MakeSwimmer(club, "Star", 2016);
        var rival = MakeSwimmer(club, "Rival", 2016);
        db.AddRange(club, comp, style, star, rival);
        db.Add(Swim(comp, club, star, style, new DateTime(2026, 2, 15), timeMs: 30_000));
        db.Add(Swim(comp, club, star, style, new DateTime(2026, 2, 15), timeMs: 32_000));
        db.Add(Swim(comp, club, rival, style, new DateTime(2026, 2, 15), timeMs: 31_000));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetSeasonBestListAsync(ListQuery(best: true));

        Assert.Equal(2, res.Total);
        Assert.Equal(["Star X", "Rival X"], res.Data.Select(i => i.Name));
        // В схлопнутом режиме места пересчитаны по получившемуся списку, а не унаследованы.
        Assert.Equal([1, 2], res.Data.Select(i => i.Place));
        Assert.All(res.Data, i => Assert.Equal(1, i.Attempt));
    }

    [Fact]
    public async Task ListSharesPlaceOnEqualTimes()
    {
        // Равные времена делят место, следующий получает свой порядковый номер: 1, 2, 2, 4.
        using var db = CreateDb(nameof(ListSharesPlaceOnEqualTimes));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var comp = MakeCompetition();
        var a = MakeSwimmer(club, "Aaa", 2016);
        var b = MakeSwimmer(club, "Bbb", 2016);
        var c = MakeSwimmer(club, "Ccc", 2016);
        var d = MakeSwimmer(club, "Ddd", 2016);
        db.AddRange(club, comp, style, a, b, c, d);
        db.Add(Swim(comp, club, a, style, new DateTime(2026, 2, 15), timeMs: 30_000));
        db.Add(Swim(comp, club, b, style, new DateTime(2026, 2, 15), timeMs: 31_000));
        db.Add(Swim(comp, club, c, style, new DateTime(2026, 2, 15), timeMs: 31_000));
        db.Add(Swim(comp, club, d, style, new DateTime(2026, 2, 15), timeMs: 33_000));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetSeasonBestListAsync(ListQuery());

        Assert.Equal([1, 2, 2, 4], res.Data.Select(i => i.Place));
    }

    [Fact]
    public async Task ListClubFilterKeepsPlacesFromFullRanking()
    {
        // Фильтр по клубу — это ПОКАЗ, а не пересчёт: пользователь видит своих пловцов с их
        // местами в общем рейтинге (#2, #4), с пропусками. Пересчёт мест внутри клуба — уже
        // другой продукт («лучшие в клубе»).
        using var db = CreateDb(nameof(ListClubFilterKeepsPlacesFromFullRanking));
        var style = FreestyleStyle();
        var ours = new Club { Name = "Ours", NameEn = "Ours" };
        var theirs = new Club { Name = "Theirs", NameEn = "Theirs" };
        var comp = MakeCompetition();
        var leader = MakeSwimmer(theirs, "Leader", 2016);
        var mine1 = MakeSwimmer(ours, "MineA", 2016);
        var other = MakeSwimmer(theirs, "Other", 2016);
        var mine2 = MakeSwimmer(ours, "MineB", 2016);
        db.AddRange(ours, theirs, comp, style, leader, mine1, other, mine2);
        db.Add(Swim(comp, theirs, leader, style, new DateTime(2026, 2, 15), timeMs: 30_000));
        db.Add(Swim(comp, ours, mine1, style, new DateTime(2026, 2, 15), timeMs: 31_000));
        db.Add(Swim(comp, theirs, other, style, new DateTime(2026, 2, 15), timeMs: 32_000));
        db.Add(Swim(comp, ours, mine2, style, new DateTime(2026, 2, 15), timeMs: 33_000));
        await db.SaveChangesAsync();

        var repo = Repo(db);
        var all = await repo.GetSeasonBestListAsync(ListQuery());
        var oursId = all.Data.Single(i => i.Name == "MineA X").ClubId;

        var res = await repo.GetSeasonBestListAsync(ListQuery(clubId: oursId));

        Assert.Equal(2, res.Total);
        Assert.Equal([2, 4], res.Data.Select(i => i.Place));
        // Опции фильтра считаются ДО фильтра по клубу — иначе, выбрав клуб, пользователь
        // больше не смог бы выбрать другой.
        Assert.Equal(2, res.Clubs.Count);
        // Отставание от лидера остаётся отставанием от ЛИДЕРА СРЕЗА, а не от лучшего в клубе.
        Assert.Equal(1_000, res.Data[0].GapMs);
    }

    [Fact]
    public async Task ListFiltersByAgeAndGender()
    {
        using var db = CreateDb(nameof(ListFiltersByAgeAndGender));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var comp = MakeCompetition();
        var girl10 = MakeSwimmer(club, "Girl10", 2016);
        girl10.Gender = "female";
        var boy10 = MakeSwimmer(club, "Boy10", 2016);
        boy10.Gender = "male";
        var girl11 = MakeSwimmer(club, "Girl11", 2015);
        girl11.Gender = "female";
        db.AddRange(club, comp, style, girl10, boy10, girl11);
        db.Add(Swim(comp, club, girl10, style, new DateTime(2026, 2, 15), timeMs: 34_000));
        db.Add(Swim(comp, club, boy10, style, new DateTime(2026, 2, 15), timeMs: 30_000));
        db.Add(Swim(comp, club, girl11, style, new DateTime(2026, 2, 15), timeMs: 31_000));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetSeasonBestListAsync(ListQuery(age: 10, gender: "female"));

        var only = Assert.Single(res.Data);
        Assert.Equal("Girl10 X", only.Name);
        Assert.Equal(10, only.Age);
        Assert.Equal("female", only.Gender);
        // Лидер среза — она сама, поэтому отставания нет: срез считается ПОСЛЕ фильтров.
        Assert.Equal(0, only.GapMs);
    }

    [Fact]
    public async Task ListPagesAndReportsTotal()
    {
        using var db = CreateDb(nameof(ListPagesAndReportsTotal));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var comp = MakeCompetition();
        db.AddRange(club, comp, style);
        for (var i = 0; i < 5; i++)
        {
            var swimmer = MakeSwimmer(club, $"S{i}", 2016);
            db.Add(swimmer);
            db.Add(Swim(comp, club, swimmer, style, new DateTime(2026, 2, 15), timeMs: 30_000 + i * 1_000));
        }
        await db.SaveChangesAsync();

        var res = await Repo(db).GetSeasonBestListAsync(ListQuery(limit: 2, offset: 2));

        Assert.Equal(5, res.Total);           // total — про весь срез, а не про страницу
        Assert.Equal(2, res.Data.Count);
        Assert.Equal([3, 4], res.Data.Select(i => i.Place));
    }

    [Fact]
    public async Task ListExcludesMastersUnlessAsked()
    {
        // Две выборки не смешиваются: обычный срез не видит мастерсов, мастерский — юниоров.
        // Иначе в одном рейтинге оказались бы 12-летние и 47-летние.
        using var db = CreateDb(nameof(ListExcludesMastersUnlessAsked));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var youthMeet = MakeCompetition();
        var mastersMeet = MakeCompetition(isMasters: true);
        var kid = MakeSwimmer(club, "Kid", 2016);
        var veteran = MakeSwimmer(club, "Veteran", 1980);
        db.AddRange(club, youthMeet, mastersMeet, style, kid, veteran);
        db.Add(Swim(youthMeet, club, kid, style, new DateTime(2026, 2, 15), timeMs: 34_000));
        var mastersSwim = Swim(mastersMeet, club, veteran, style, new DateTime(2026, 2, 15), timeMs: 30_000);
        mastersSwim.AgeGroup = "45-49";
        db.Add(mastersSwim);
        await db.SaveChangesAsync();

        var youth = await Repo(db).GetSeasonBestListAsync(ListQuery());
        var masters = await Repo(db).GetSeasonBestListAsync(ListQuery(masters: true));

        Assert.Equal("Kid X", Assert.Single(youth.Data).Name);
        Assert.False(youth.Masters);

        var only = Assert.Single(masters.Data);
        Assert.Equal("Veteran X", only.Name);
        Assert.Equal("45-49", only.AgeGroup);   // группа едет в строку: без неё не видно круга ровесников
        Assert.True(masters.Masters);
    }

    [Fact]
    public async Task ListMastersRanksInsideAgeGroup()
    {
        // У мастерсов ровесники — это ГРУППА, а не год рождения: место считается внутри
        // пятилетки, и чужая группа в срез не попадает, даже если проплыла быстрее.
        using var db = CreateDb(nameof(ListMastersRanksInsideAgeGroup));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var meet = MakeCompetition(isMasters: true);
        var young = MakeSwimmer(club, "Young", 1998);
        var older = MakeSwimmer(club, "Older", 1980);
        var sameGroup = MakeSwimmer(club, "SameGroup", 1979);
        db.AddRange(club, meet, style, young, older, sameGroup);
        var fastOtherGroup = Swim(meet, club, young, style, new DateTime(2026, 2, 15), timeMs: 25_000);
        fastOtherGroup.AgeGroup = "25-29";
        var inGroupFast = Swim(meet, club, older, style, new DateTime(2026, 2, 15), timeMs: 30_000);
        inGroupFast.AgeGroup = "45-49";
        var inGroupSlow = Swim(meet, club, sameGroup, style, new DateTime(2026, 2, 15), timeMs: 33_000);
        inGroupSlow.AgeGroup = "45-49";
        db.AddRange(fastOtherGroup, inGroupFast, inGroupSlow);
        await db.SaveChangesAsync();

        var res = await Repo(db).GetSeasonBestListAsync(ListQuery(masters: true, ageGroup: "45-49"));

        Assert.Equal(["Older X", "SameGroup X"], res.Data.Select(i => i.Name));
        Assert.Equal([1, 2], res.Data.Select(i => i.Place));
        Assert.Equal("45-49", res.AgeGroup);
        // Возрастные границы обычного среза в мастерском режиме не применяются и наружу
        // не отдаются — иначе витрина показала бы «возраст 47» как фильтр, которого не было.
        Assert.Null(res.Age);
        Assert.Null(res.AgeTo);
    }

    [Fact]
    public async Task ListAgeRangeCoversAdultTail()
    {
        // Кнопка «21+» на витрине — это диапазон age=21..99: без верхней границы взрослые
        // в обычных стартах были недостижимы фильтром вовсе.
        using var db = CreateDb(nameof(ListAgeRangeCoversAdultTail));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var comp = MakeCompetition();
        var teen = MakeSwimmer(club, "Teen", 2009);        // 17 в сезоне 2025/26
        var adult21 = MakeSwimmer(club, "Adult21", 2005);  // 21
        var adult30 = MakeSwimmer(club, "Adult30", 1996);  // 30
        db.AddRange(club, comp, style, teen, adult21, adult30);
        db.Add(Swim(comp, club, teen, style, new DateTime(2026, 2, 15), timeMs: 29_000));
        db.Add(Swim(comp, club, adult21, style, new DateTime(2026, 2, 15), timeMs: 30_000));
        db.Add(Swim(comp, club, adult30, style, new DateTime(2026, 2, 15), timeMs: 31_000));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetSeasonBestListAsync(ListQuery(age: 21, ageTo: 99));

        Assert.Equal(["Adult21 X", "Adult30 X"], res.Data.Select(i => i.Name));
        Assert.Equal([21, 30], res.Data.Select(i => i.Age));
    }

    [Fact]
    public async Task OptionsListMastersAgeGroupsInAgeOrder()
    {
        using var db = CreateDb(nameof(OptionsListMastersAgeGroupsInAgeOrder));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var youthMeet = MakeCompetition();
        var mastersMeet = MakeCompetition(isMasters: true);
        var kid = MakeSwimmer(club, "Kid", 2016);
        db.AddRange(club, youthMeet, mastersMeet, style, kid);
        // У юниорского заплыва группа тоже бывает заполнена — в мастерскую шкалу она попасть
        // не должна, иначе во второй шкале появятся детские «11-12».
        var youthSwim = Swim(youthMeet, club, kid, style, new DateTime(2026, 2, 15));
        youthSwim.AgeGroup = "11-12";
        db.Add(youthSwim);
        // Возраст пловца должен сходиться с его группой — иначе подпись отбрасывается как
        // мусор протокола (см. OptionsDropAgeGroupsThatDoNotMatchTheirSwimmers).
        foreach (var (group, birthYear) in new[] { ("100+", 1920), ("25-29", 1999), ("19-24", 2004) })
        {
            var swimmer = MakeSwimmer(club, "M" + group, birthYear);
            db.Add(swimmer);
            var swim = Swim(mastersMeet, club, swimmer, style, new DateTime(2026, 2, 15));
            swim.AgeGroup = group;
            db.Add(swim);
        }
        await db.SaveChangesAsync();

        var res = await Repo(db).GetSeasonBestOptionsAsync();

        // Порядок по НИЖНЕЙ границе, а не строковый: «100+» иначе встал бы первым.
        Assert.Equal(["19-24", "25-29", "100+"], res.AgeGroups);
    }

    [Fact]
    public async Task OptionsDropAgeGroupsThatDoNotMatchTheirSwimmers()
    {
        // Живой случай: у соревнования «ליגה מאסטרס - וייסגל רחובות» ВСЕ строки помечены
        // группой «9-11», а плывут там взрослые. Такая подпись — мусор протокола, и в
        // селекторе витрины ей не место, хотя в базе она остаётся как есть.
        using var db = CreateDb(nameof(OptionsDropAgeGroupsThatDoNotMatchTheirSwimmers));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var meet = MakeCompetition(isMasters: true);
        var adult = MakeSwimmer(club, "Adult", 1979);   // 47 в сезоне 2025/26
        db.AddRange(club, meet, style, adult);
        foreach (var group in new[] { "9-11", "45-49" })
        {
            var swim = Swim(meet, club, adult, style, new DateTime(2026, 2, 15));
            swim.AgeGroup = group;
            db.Add(swim);
        }
        await db.SaveChangesAsync();

        var res = await Repo(db).GetSeasonBestOptionsAsync();

        Assert.Equal(["45-49"], res.AgeGroups);
    }

    [Fact]
    public async Task ListExcludesOpenWaterSwims()
    {
        // Морская трёшка и бассейновая — разные старты: дистанция совпадает, а время
        // несравнимо. В один рейтинг их ставить нельзя (docs/data-integrity.md §9, Р24).
        using var db = CreateDb(nameof(ListExcludesOpenWaterSwims));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var pool = MakeCompetition();
        var sea = MakeCompetition(standingKindOverride: "openwater");
        var poolSwimmer = MakeSwimmer(club, "PoolSwimmer", 2010);
        var seaSwimmer = MakeSwimmer(club, "SeaSwimmer", 2010);
        db.AddRange(club, pool, sea, style, poolSwimmer, seaSwimmer);
        db.Add(Swim(pool, club, poolSwimmer, style, new DateTime(2026, 2, 15), timeMs: 2_400_000, distance: "3000"));
        // Морской заплыв быстрее — без фильтра он забрал бы первое место.
        db.Add(Swim(sea, club, seaSwimmer, style, new DateTime(2026, 2, 15), timeMs: 2_100_000, distance: "3000"));
        await db.SaveChangesAsync();

        var query = ListQuery();
        query.Distance = "3000";
        var res = await Repo(db).GetSeasonBestListAsync(query);

        var only = Assert.Single(res.Data);
        Assert.Equal("PoolSwimmer X", only.Name);
    }

    [Fact]
    public async Task OptionsHideOpenWaterDistances()
    {
        // Живой случай: чемпионат в открытой воде (#1547, Эйлат) приносил в селектор
        // дисциплины 1600/5000/10000 — дистанции, которых в бассейне не плавают.
        using var db = CreateDb(nameof(OptionsHideOpenWaterDistances));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var pool = MakeCompetition();
        var sea = MakeCompetition(standingKindOverride: "openwater");
        var swimmer = MakeSwimmer(club, "Any", 2010);
        db.AddRange(club, pool, sea, style, swimmer);
        db.Add(Swim(pool, club, swimmer, style, new DateTime(2026, 2, 15), distance: "50"));
        db.Add(Swim(pool, club, swimmer, style, new DateTime(2026, 2, 15), distance: "3000"));
        db.Add(Swim(sea, club, swimmer, style, new DateTime(2026, 2, 15), distance: "5000"));
        db.Add(Swim(sea, club, swimmer, style, new DateTime(2026, 2, 15), distance: "10000"));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetSeasonBestOptionsAsync();

        // 3000 остаётся: её плавают и в бассейне (чемпионат «3 ק"מ בבריכה»), а 5000/10000 — нет.
        Assert.Equal(["50", "3000"], Assert.Single(res.Events).Distances);
    }

    [Fact]
    public async Task NationalSeasonBestExcludesOpenWater()
    {
        using var db = CreateDb(nameof(NationalSeasonBestExcludesOpenWater));
        var style = FreestyleStyle();
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var pool = MakeCompetition();
        var sea = MakeCompetition(standingKindOverride: "openwater");
        var poolSwimmer = MakeSwimmer(club, "PoolSwimmer", 2010);
        var seaSwimmer = MakeSwimmer(club, "SeaSwimmer", 2010);
        db.AddRange(club, pool, sea, style, poolSwimmer, seaSwimmer);
        db.Add(Swim(pool, club, poolSwimmer, style, new DateTime(2026, 2, 15), timeMs: 2_400_000, distance: "3000"));
        db.Add(Swim(sea, club, seaSwimmer, style, new DateTime(2026, 2, 15), timeMs: 2_100_000, distance: "3000"));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetNationalSeasonBestAsync("freestyle", "3000", null, Season);

        var only = Assert.Single(res.Data);
        Assert.Equal("PoolSwimmer X", only.Name);
    }

    [Fact]
    public async Task OptionsListSeasonsAndCanonicalEvents()
    {
        using var db = CreateDb(nameof(OptionsListSeasonsAndCanonicalEvents));
        var style = FreestyleStyle();
        // Мусорный ключ стиля из кривого протокола в селектор дисциплины попасть не должен.
        var junkStyle = new Style { Id = 101, Name = "מטר_חופשי" };
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var thisSeason = MakeCompetition(date: "15/02/2026");
        var lastSeason = MakeCompetition(date: "15/02/2025");
        var swimmer = MakeSwimmer(club, "Any", 2016);
        db.AddRange(club, thisSeason, lastSeason, style, junkStyle, swimmer);
        db.Add(Swim(thisSeason, club, swimmer, style, new DateTime(2026, 2, 15)));
        db.Add(Swim(thisSeason, club, swimmer, style, new DateTime(2026, 2, 15), distance: "100"));
        db.Add(Swim(thisSeason, club, swimmer, junkStyle, new DateTime(2026, 2, 15)));
        db.Add(Swim(lastSeason, club, swimmer, style, new DateTime(2025, 2, 15)));
        await db.SaveChangesAsync();

        var res = await Repo(db).GetSeasonBestOptionsAsync();

        Assert.Equal([2025, 2024], res.Seasons.Select(s => s.Season));
        Assert.True(res.Seasons[0].IsDisplayDefault);   // умолчание — самый свежий сезон
        var freestyle = Assert.Single(res.Events);      // мусорный стиль отфильтрован
        Assert.Equal("freestyle", freestyle.Style);
        Assert.Equal(["50", "100"], freestyle.Distances);  // дистанции по возрастанию
    }
}
