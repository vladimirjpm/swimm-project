using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="ClubPublicRepository"/> — ростер клуба и клубные рекорды (K4.2,
/// docs/plans/club-page-plan.md). Ось рекордов скопирована с «рекордов группы»
/// (HubGroupPublicRepository, фаза 8.3) + дополнительный фильтр SuspectReason.
/// </summary>
public class ClubPublicRepositoryTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ClubPublicRepository Repo(SwimmReadDbContext db) => new(db);

    private static Swimmer MakeSwimmer(
        Club club, string lastNameEn, string firstNameEn, int birthYear, string? gender = "M") => new()
    {
        Club = club,
        LastName = lastNameEn,
        FirstName = firstNameEn,
        LastNameEn = lastNameEn,
        FirstNameEn = firstNameEn,
        BirthYear = birthYear,
        Gender = gender
    };

    /// <summary>StyleId = 100 «freestyle» — сеять ОДИН раз на тест (см. <see cref="Swim"/>),
    /// иначе несколько ResultRecord со своим Style-объектом одного Id дают конфликт трекера EF.</summary>
    private static Style FreestyleStyle() => new() { Id = 100, Name = "freestyle" };

    private static ResultRecord Swim(
        Competition comp, Club club, Swimmer swimmer, DateTime date,
        int? timeMs = 60_000, string distance = "100", string gender = "male",
        bool timeFail = false, int? relayId = null, string? suspectReason = null,
        int styleId = 100) => new()
    {
        Competition = comp,
        Club = club,
        Swimmer = swimmer,
        StyleId = styleId,
        RelayId = relayId,
        CompetitionDate = date,
        Distance = distance,
        Gender = gender,
        TimeFail = timeFail,
        TimeMillisecond = timeFail ? null : timeMs,
        TimeOriginal = timeFail ? "" : $"{timeMs / 1000.0:0.00}",
        SuspectReason = suspectReason,
        InternationalPoints = 700
    };

    private static Competition MakeCompetition(string poolType, string date = "15/02/2026") =>
        new() { Name = "Meet", Date = date, PoolType = poolType };

    /* ─────────────────────────── Ростер ─────────────────────────── */

    [Fact]
    public async Task Roster_Paginates()
    {
        using var db = CreateDb(nameof(Roster_Paginates));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        db.Add(club);
        for (var i = 0; i < 5; i++)
            db.Add(MakeSwimmer(club, $"Swimmer{i}", "X", 2010));
        await db.SaveChangesAsync();

        var page1 = await Repo(db).GetRosterAsync(club.Id, page: 1, pageSize: 2, gender: null, ageFrom: null, ageTo: null, season: null);
        var page3 = await Repo(db).GetRosterAsync(club.Id, page: 3, pageSize: 2, gender: null, ageFrom: null, ageTo: null, season: null);

        Assert.Equal(5, page1.Total);
        Assert.Equal(2, page1.Data.Count);
        Assert.True(page1.HasMore);
        Assert.Single(page3.Data); // последняя страница — 1 запись из 5
        Assert.False(page3.HasMore);
    }

    [Fact]
    public async Task Roster_FiltersByGender()
    {
        using var db = CreateDb(nameof(Roster_FiltersByGender));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        db.Add(club);
        db.Add(MakeSwimmer(club, "Male", "One", 2010, gender: "M"));
        db.Add(MakeSwimmer(club, "Female", "One", 2010, gender: "F"));
        await db.SaveChangesAsync();

        var result = await Repo(db).GetRosterAsync(club.Id, 1, 50, gender: "female", ageFrom: null, ageTo: null, season: null);

        var row = Assert.Single(result.Data);
        Assert.Equal("Female", row.LastNameEn);
        Assert.Equal("female", row.Gender); // API отдаёт male/female, не M/F
    }

    [Fact]
    public async Task Roster_FiltersByGender_WhenStoredAsWords()
    {
        // В БД Swimmer.Gender живёт в ДВУХ форматах: "male"/"female" (подавляющее
        // большинство — 2475/1484 на 2026-08-01) и "M"/"F" (13/2 от старого импорта).
        // Фильтр обязан ловить оба, иначе ростер по полу почти всегда пуст.
        using var db = CreateDb(nameof(Roster_FiltersByGender_WhenStoredAsWords));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        db.Add(club);
        db.Add(MakeSwimmer(club, "Male", "One", 2010, gender: "male"));
        db.Add(MakeSwimmer(club, "Female", "One", 2010, gender: "female"));
        db.Add(MakeSwimmer(club, "Nobody", "One", 2010, gender: "none"));
        await db.SaveChangesAsync();

        var females = await Repo(db).GetRosterAsync(club.Id, 1, 50, "female", null, null, null);
        var males = await Repo(db).GetRosterAsync(club.Id, 1, 50, "male", null, null, null);

        Assert.Equal("Female", Assert.Single(females.Data).LastNameEn);
        Assert.Equal("Male", Assert.Single(males.Data).LastNameEn);
    }

    [Fact]
    public async Task Roster_FiltersByAge_UsingGivenSeason()
    {
        using var db = CreateDb(nameof(Roster_FiltersByAge_UsingGivenSeason));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        db.Add(club);
        // Возраст в сезоне = год ОКОНЧАНИЯ сезона минус год рождения (SeasonMath.AgeInSeason):
        // сезон 2025/26 считается по 2026 году.
        db.Add(MakeSwimmer(club, "Young", "One", 2015)); // возраст в сезоне 2025/26 = 11
        db.Add(MakeSwimmer(club, "Old", "One", 2010));   // возраст в сезоне 2025/26 = 16
        await db.SaveChangesAsync();

        var result = await Repo(db).GetRosterAsync(club.Id, 1, 50, gender: null, ageFrom: 11, ageTo: 11, season: 2025);

        var row = Assert.Single(result.Data);
        Assert.Equal("Young", row.LastNameEn);
        Assert.Equal(11, row.Age);
    }

    [Fact]
    public async Task Roster_Age_ComputedFromGivenSeason_NotToday()
    {
        using var db = CreateDb(nameof(Roster_Age_ComputedFromGivenSeason_NotToday));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        db.Add(club);
        db.Add(MakeSwimmer(club, "Swimmer", "One", 2000));
        await db.SaveChangesAsync();

        // season=2020 передан явно — возраст обязан считаться от него, а не от сегодняшней
        // даты. Сезон 2020/21 → возраст по 2021 году: 2021 - 2000 = 21.
        var result = await Repo(db).GetRosterAsync(club.Id, 1, 50, gender: null, ageFrom: null, ageTo: null, season: 2020);

        Assert.Equal(21, Assert.Single(result.Data).Age);
    }

    [Fact]
    public async Task Roster_CountsCompetitionsAndSwims_ScopedToClubAndOptionallySeason()
    {
        using var db = CreateDb(nameof(Roster_CountsCompetitionsAndSwims_ScopedToClubAndOptionallySeason));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = MakeSwimmer(club, "Swimmer", "One", 2010);
        db.AddRange(club, swimmer);
        var compA = MakeCompetition("25m", "15/02/2026");
        var compB = MakeCompetition("25m", "20/07/2026");
        db.AddRange(compA, compB, FreestyleStyle());
        await db.SaveChangesAsync();

        db.AddRange(
            Swim(compA, club, swimmer, new DateTime(2026, 2, 15), distance: "50"),
            Swim(compA, club, swimmer, new DateTime(2026, 2, 15), distance: "100"), // тот же старт, второй заплыв
            Swim(compB, club, swimmer, new DateTime(2026, 7, 20), distance: "50")); // другой старт, но вне сезона 2025
        await db.SaveChangesAsync();

        var noSeason = await Repo(db).GetRosterAsync(club.Id, 1, 50, null, null, null, season: null);
        var row = Assert.Single(noSeason.Data);
        Assert.Equal(2, row.Competitions);
        Assert.Equal(3, row.Swims);

        // Сезон 2025 (1 сен 2025 – 31 авг 2026) включает ОБА старта — уточним границей поуже.
        var seasonScoped = await Repo(db).GetRosterAsync(club.Id, 1, 50, null, null, null, season: 2025);
        var scopedRow = Assert.Single(seasonScoped.Data);
        Assert.Equal(2, scopedRow.Competitions);
        Assert.Equal(3, scopedRow.Swims);
    }

    [Fact]
    public async Task Roster_Season_ExcludesSwimsOutsideRange()
    {
        using var db = CreateDb(nameof(Roster_Season_ExcludesSwimsOutsideRange));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = MakeSwimmer(club, "Swimmer", "One", 2010);
        db.AddRange(club, swimmer);
        var compInSeason = MakeCompetition("25m", "15/02/2026");
        var compBeforeSeason = MakeCompetition("25m", "15/02/2025");
        db.AddRange(compInSeason, compBeforeSeason, FreestyleStyle());
        await db.SaveChangesAsync();

        db.AddRange(
            Swim(compInSeason, club, swimmer, new DateTime(2026, 2, 15)),
            Swim(compBeforeSeason, club, swimmer, new DateTime(2025, 2, 15))); // сезон 2024, не 2025
        await db.SaveChangesAsync();

        var result = await Repo(db).GetRosterAsync(club.Id, 1, 50, null, null, null, season: 2025);

        var row = Assert.Single(result.Data);
        Assert.Equal(1, row.Competitions);
        Assert.Equal(1, row.Swims);
    }

    /* ────────── Season best: «наши первые в Израиле» за сезон ────────── */

    [Fact]
    public async Task SeasonBest_KeepsOnlySlotsWhereClubSwimmerLeadsTheCountry()
    {
        using var db = CreateDb(nameof(SeasonBest_KeepsOnlySlotsWhereClubSwimmerLeadsTheCountry));
        var ours = new Club { Name = "Alpha", NameEn = "Alpha" };
        var rival = new Club { Name = "Beta", NameEn = "Beta" };
        var ourKid = MakeSwimmer(ours, "Ours", "One", 2016);      // 10 лет в 2026
        var rivalKid = MakeSwimmer(rival, "Rival", "One", 2016);  // тот же возраст
        db.AddRange(ours, rival, ourKid, rivalKid);
        var comp = MakeCompetition("25m");
        db.AddRange(comp, FreestyleStyle());
        await db.SaveChangesAsync();

        // Слот один (25м, freestyle, 100, male, age 10) — и в нём быстрее чужой пловец.
        db.AddRange(
            Swim(comp, rival, rivalKid, new DateTime(2026, 2, 15), timeMs: 55_000),
            Swim(comp, ours, ourKid, new DateTime(2026, 2, 15), timeMs: 60_000));
        await db.SaveChangesAsync();

        var result = await Repo(db).GetSeasonBestAsync(ours.Id, poolType: null, season: 2025);

        // Наше время лучшее в клубе, но НЕ лучшее в стране — карточка молчит.
        Assert.Empty(result.Data);
        Assert.Equal(0, result.Total);

        // У соперника тот же слот — лидерский, значит его карточка его покажет.
        var rivalCard = await Repo(db).GetSeasonBestAsync(rival.Id, poolType: null, season: 2025);
        Assert.Equal(55_000, Assert.Single(Assert.Single(rivalCard.Data).Items).TimeMs);
    }

    [Fact]
    public async Task SeasonBest_AgeStepsAreSeparateSlots_SoAKidLeadsEvenIfAdultIsFaster()
    {
        using var db = CreateDb(nameof(SeasonBest_AgeStepsAreSeparateSlots_SoAKidLeadsEvenIfAdultIsFaster));
        var ours = new Club { Name = "Alpha", NameEn = "Alpha" };
        var rival = new Club { Name = "Beta", NameEn = "Beta" };
        var ourKid = MakeSwimmer(ours, "Kid", "One", 2016);        // 10 лет
        var rivalAdult = MakeSwimmer(rival, "Adult", "One", 2003); // 23 года, быстрее всех
        db.AddRange(ours, rival, ourKid, rivalAdult);
        var comp = MakeCompetition("25m");
        db.AddRange(comp, FreestyleStyle());
        await db.SaveChangesAsync();

        db.AddRange(
            Swim(comp, rival, rivalAdult, new DateTime(2026, 2, 15), timeMs: 37_260),
            Swim(comp, ours, ourKid, new DateTime(2026, 2, 15), timeMs: 60_000));
        await db.SaveChangesAsync();

        var result = await Repo(db).GetSeasonBestAsync(ours.Id, poolType: null, season: 2025);

        // Возраст — часть слота, поэтому взрослый чужого клуба ребёнка не перекрывает.
        var tile = Assert.Single(Assert.Single(result.Data).Items);
        Assert.Equal("10", tile.AgeKey);
        Assert.Equal("age 10", tile.AgeLabel);
        Assert.Equal(60_000, tile.TimeMs);
    }

    [Fact]
    public async Task SeasonBest_AgeIsTakenFromSeason_NotFromSwimDate()
    {
        using var db = CreateDb(nameof(SeasonBest_AgeIsTakenFromSeason_NotFromSwimDate));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        // 2017 г.р.: в 2026 исполняется 9 ⇒ в сезоне 2025/26 он «age 9» на ЛЮБОМ старте,
        // включая декабрьский 2025 (правило Влада 2026-08-01). По дате заплыва вышло бы 8.
        var swimmer = MakeSwimmer(club, "Swimmer", "One", 2017);
        db.AddRange(club, swimmer);
        var december = MakeCompetition("25m", date: "10/12/2025");
        var february = MakeCompetition("25m", date: "15/02/2026");
        db.AddRange(december, february, FreestyleStyle());
        await db.SaveChangesAsync();

        db.AddRange(
            Swim(december, club, swimmer, new DateTime(2025, 12, 10), timeMs: 60_000),
            Swim(february, club, swimmer, new DateTime(2026, 2, 15), timeMs: 58_000));
        await db.SaveChangesAsync();

        var section = Assert.Single((await Repo(db).GetSeasonBestAsync(club.Id, null, 2025)).Data);

        // Оба старта в одной ступени, поэтому плитка одна — с лучшим из двух времён.
        var tile = Assert.Single(section.Items);
        Assert.Equal("9", tile.AgeKey);
        Assert.Equal(58_000, tile.TimeMs);
    }

    [Fact]
    public async Task SeasonBest_25mAnd50m_AreSeparateSlots()
    {
        using var db = CreateDb(nameof(SeasonBest_25mAnd50m_AreSeparateSlots));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = MakeSwimmer(club, "Swimmer", "One", 2010);
        db.AddRange(club, swimmer);
        var comp25 = MakeCompetition("25m");
        var comp50 = MakeCompetition("50m");
        db.AddRange(comp25, comp50, FreestyleStyle());
        await db.SaveChangesAsync();

        db.AddRange(
            Swim(comp25, club, swimmer, new DateTime(2026, 2, 15), timeMs: 55_000),
            Swim(comp50, club, swimmer, new DateTime(2026, 2, 15), timeMs: 58_000));
        await db.SaveChangesAsync();

        var both = await Repo(db).GetSeasonBestAsync(club.Id, poolType: null, season: 2025);
        Assert.Equal(2, both.Data.Count);
        Assert.Contains(both.Data, g => g.PoolType == "25m" && g.Items[0].TimeMs == 55_000);
        Assert.Contains(both.Data, g => g.PoolType == "50m" && g.Items[0].TimeMs == 58_000);

        var only25 = await Repo(db).GetSeasonBestAsync(club.Id, poolType: "25m", season: 2025);
        var section = Assert.Single(only25.Data);
        Assert.Equal("25m", section.PoolType);
        Assert.Equal(55_000, Assert.Single(section.Items).TimeMs);
    }

    [Fact]
    public async Task SeasonBest_MastersInFiveYearBands_UnknownBirthYearLast()
    {
        using var db = CreateDb(nameof(SeasonBest_MastersInFiveYearBands_UnknownBirthYearLast));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var m45 = MakeSwimmer(club, "Masters", "One", 1981);   // 45 лет в 2026
        var m47 = MakeSwimmer(club, "Masters", "Two", 1979);   // 47 — та же пятилетка
        var m52 = MakeSwimmer(club, "Masters", "Three", 1974); // 52 — следующая
        var unknown = MakeSwimmer(club, "NoYear", "One", 0);   // год рождения не заполнен
        db.AddRange(club, m45, m47, m52, unknown);
        var comp = MakeCompetition("25m");
        db.AddRange(comp, FreestyleStyle());
        await db.SaveChangesAsync();

        db.AddRange(
            Swim(comp, club, m45, new DateTime(2026, 2, 15), timeMs: 40_000),
            Swim(comp, club, m47, new DateTime(2026, 2, 15), timeMs: 39_000),
            Swim(comp, club, m52, new DateTime(2026, 2, 15), timeMs: 41_000),
            Swim(comp, club, unknown, new DateTime(2026, 2, 15), timeMs: 42_000));
        await db.SaveChangesAsync();

        var section = Assert.Single((await Repo(db).GetSeasonBestAsync(club.Id, null, 2025)).Data);

        var band45 = section.Items.Single(i => i.AgeKey == "45-49");
        Assert.Equal(39_000, band45.TimeMs); // 45 и 47 в одной пятилетке — берём быстрейшего
        Assert.Equal("masters 45-49", band45.AgeLabel);
        Assert.Contains(section.Items, i => i.AgeKey == "50-54");
        // Без года рождения заплыв не выбрасывается, а уходит в конец ступенью «n/a».
        Assert.Equal("n/a", section.Items[^1].AgeKey);
    }

    [Fact]
    public async Task SeasonBest_ExcludeTimeFail_Relays_And_SuspectReason()
    {
        using var db = CreateDb(nameof(SeasonBest_ExcludeTimeFail_Relays_And_SuspectReason));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmerFail = MakeSwimmer(club, "Fail", "One", 2010);
        var swimmerRelay = MakeSwimmer(club, "Relay", "One", 2010);
        var swimmerSuspect = MakeSwimmer(club, "Suspect", "One", 2010);
        var swimmerValid = MakeSwimmer(club, "Valid", "One", 2010);
        db.AddRange(club, swimmerFail, swimmerRelay, swimmerSuspect, swimmerValid);
        var comp = MakeCompetition("25m");
        var relay = new Relay { TeamName = "Alpha A" };
        db.AddRange(comp, relay, FreestyleStyle());
        await db.SaveChangesAsync();

        // Три «испорченных» строки нарочно БЫСТРЕЕ валидной — если фильтр не сработает,
        // лидером страны станет одна из них (все пловцы одного возраста, слот общий).
        db.AddRange(
            Swim(comp, club, swimmerFail, new DateTime(2026, 2, 15), timeMs: 50_000, timeFail: true),
            Swim(comp, club, swimmerRelay, new DateTime(2026, 2, 15), timeMs: 51_000, relayId: relay.Id),
            Swim(comp, club, swimmerSuspect, new DateTime(2026, 2, 15), timeMs: 52_000, suspectReason: "protocol-error"),
            Swim(comp, club, swimmerValid, new DateTime(2026, 2, 15), timeMs: 60_000));
        await db.SaveChangesAsync();

        var section = Assert.Single((await Repo(db).GetSeasonBestAsync(club.Id, null, 2025)).Data);

        var row = Assert.Single(section.Items);
        Assert.Equal(swimmerValid.Id, row.SwimmerId);
        Assert.Equal(60_000, row.TimeMs);
    }

    [Fact]
    public async Task SeasonBest_TieBreak_EarlierSwimWins()
    {
        using var db = CreateDb(nameof(SeasonBest_TieBreak_EarlierSwimWins));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var earlier = MakeSwimmer(club, "Earlier", "One", 2010);
        var later = MakeSwimmer(club, "Later", "One", 2010);
        db.AddRange(club, earlier, later);
        var comp = MakeCompetition("25m");
        db.AddRange(comp, FreestyleStyle());
        await db.SaveChangesAsync();

        db.AddRange(
            Swim(comp, club, later, new DateTime(2026, 2, 20), timeMs: 55_000),
            Swim(comp, club, earlier, new DateTime(2026, 2, 10), timeMs: 55_000)); // то же время, раньше дата
        await db.SaveChangesAsync();

        var section = Assert.Single((await Repo(db).GetSeasonBestAsync(club.Id, null, 2025)).Data);

        Assert.Equal(earlier.Id, Assert.Single(section.Items).SwimmerId);
    }

    [Fact]
    public async Task SeasonBest_KeepsOnlySwimsOfThatSeason_AndReportsSeasonAndMeets()
    {
        using var db = CreateDb(nameof(SeasonBest_KeepsOnlySwimsOfThatSeason_AndReportsSeasonAndMeets));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        var swimmer = MakeSwimmer(club, "Swimmer", "One", 2010);
        db.AddRange(club, swimmer);
        var compOld = MakeCompetition("25m", date: "15/02/2025");
        var compNew = MakeCompetition("25m", date: "15/02/2026");
        db.AddRange(compOld, compNew, FreestyleStyle());
        await db.SaveChangesAsync();

        // Сезон = 1 сентября — 31 августа (SeasonMath): 15/02/2026 лежит в сезоне 2025,
        // а 15/02/2025 — в сезоне 2024. Быстрейший заплыв нарочно в ЧУЖОМ сезоне.
        db.AddRange(
            Swim(compOld, club, swimmer, new DateTime(2025, 2, 15), timeMs: 50_000),
            Swim(compNew, club, swimmer, new DateTime(2026, 2, 15), timeMs: 60_000));
        await db.SaveChangesAsync();

        var season2025 = await Repo(db).GetSeasonBestAsync(club.Id, poolType: null, season: 2025);
        Assert.Equal(2025, season2025.Season);
        Assert.Equal("2025/26", season2025.SeasonLabel);
        // Сколько стартов вошло в расчёт — карточка обязана это показать: «первый в
        // Израиле» у нас значит «первый среди импортированного».
        Assert.Equal(1, season2025.Meets);
        Assert.Equal(60_000, Assert.Single(Assert.Single(season2025.Data).Items).TimeMs);

        // Прошлый сезон доступен явным параметром; «за всё время» карточка не умеет.
        var season2024 = await Repo(db).GetSeasonBestAsync(club.Id, poolType: null, season: 2024);
        Assert.Equal(50_000, Assert.Single(Assert.Single(season2024.Data).Items).TimeMs);
    }

    /* ─────────────── Стена официальных рекордов ─────────────── */

    // Полное имя типа: Xunit.Record тоже видно в этом файле.
    private static Swimm.Domain.Entities.Record OfficialRecord(
        string club, string category = "age", string ageKey = "10",
        string poolType = "25m", string regionType = "country", string regionCode = "ISR") => new()
    {
        RegionType = regionType,
        RegionCode = regionCode,
        Category = category,
        AgeKey = ageKey,
        Gender = "female",
        PoolType = poolType,
        Style = "freestyle",
        Distance = "100m",
        Time = "01:00.00",
        HolderName = "Holder",
        Club = club
    };

    [Fact]
    public async Task RecordWall_MatchesClubName_ExactAndWithSourceSuffix()
    {
        using var db = CreateDb(nameof(RecordWall_MatchesClubName_ExactAndWithSourceSuffix));
        var club = new Club { Name = "Hapoel Dolphin Netanya", NameEn = "Dolphin Netanya" };
        db.Add(club);
        db.AddRange(
            OfficialRecord("Hapoel Dolphin Netanya"),                    // точное имя
            OfficialRecord("Hapoel Dolphin Netanya Olympic", ageKey: "11"), // имя + суффикс источника
            OfficialRecord("Hapoel Dolphin Nahariya", ageKey: "12"));    // другой клуб — не наш
        await db.SaveChangesAsync();

        var wall = await Repo(db).GetRecordWallAsync(club.Id, poolType: null);

        Assert.Equal(2, wall.Data.Count);
        Assert.DoesNotContain(wall.Data, r => r.Club.Contains("Nahariya"));
        Assert.Contains("Hapoel Dolphin Netanya", wall.MatchedNames);
    }

    [Fact]
    public async Task RecordWall_IncludesNamesOfMergedDuplicates()
    {
        using var db = CreateDb(nameof(RecordWall_IncludesNamesOfMergedDuplicates));
        var canonical = new Club { Name = "Alpha", NameEn = "Alpha" };
        db.Add(canonical);
        await db.SaveChangesAsync();
        // Дубль склеен в канон: результаты переехали, а строки Records остались с его именем.
        db.Add(new Club { Name = "Alpha Old", NameEn = "Alpha Old", MergedIntoId = canonical.Id });
        db.Add(OfficialRecord("Alpha Old"));
        await db.SaveChangesAsync();

        var wall = await Repo(db).GetRecordWallAsync(canonical.Id, poolType: null);

        Assert.Equal("Alpha Old", Assert.Single(wall.Data).Club);
    }

    [Fact]
    public async Task RecordWall_FiltersByPool_AndOrdersOpenBeforeAgeBeforeMasters()
    {
        using var db = CreateDb(nameof(RecordWall_FiltersByPool_AndOrdersOpenBeforeAgeBeforeMasters));
        var club = new Club { Name = "Alpha", NameEn = "Alpha" };
        db.Add(club);
        db.AddRange(
            OfficialRecord("Alpha", category: "masters", ageKey: "25-29"),
            OfficialRecord("Alpha", category: "age", ageKey: "10"),
            OfficialRecord("Alpha", category: "open", ageKey: ""),
            OfficialRecord("Alpha", category: "age", ageKey: "11", poolType: "50m"));
        await db.SaveChangesAsync();

        var all = await Repo(db).GetRecordWallAsync(club.Id, poolType: null);
        Assert.Equal(["open", "age", "age", "masters"], all.Data.Select(r => r.Category).ToArray());

        var only25 = await Repo(db).GetRecordWallAsync(club.Id, poolType: "25m");
        Assert.Equal(3, only25.Data.Count);
        Assert.All(only25.Data, r => Assert.Equal("25m", r.PoolType));
    }

    /* ──────────────────────── Резолв клуба ──────────────────────── */

    [Fact]
    public async Task ResolveClubIdAsync_UnknownClub_ReturnsNull()
    {
        using var db = CreateDb(nameof(ResolveClubIdAsync_UnknownClub_ReturnsNull));
        Assert.Null(await Repo(db).ResolveClubIdAsync(9999));
    }

    [Fact]
    public async Task ResolveClubIdAsync_PseudoClub_ReturnsNull()
    {
        using var db = CreateDb(nameof(ResolveClubIdAsync_PseudoClub_ReturnsNull));
        var pseudo = new Club { Name = "Israel", NameEn = "Israel", IsPseudo = true };
        db.Add(pseudo);
        await db.SaveChangesAsync();

        Assert.Null(await Repo(db).ResolveClubIdAsync(pseudo.Id));
    }

    [Fact]
    public async Task ResolveClubIdAsync_MergedClub_ReturnsReceiverId_AndReceiverDataIsServed()
    {
        using var db = CreateDb(nameof(ResolveClubIdAsync_MergedClub_ReturnsReceiverId_AndReceiverDataIsServed));
        var receiver = new Club { Name = "Beta", NameEn = "Beta" };
        db.Add(receiver);
        await db.SaveChangesAsync();
        var merged = new Club { Name = "Alpha", NameEn = "Alpha", MergedIntoId = receiver.Id };
        var swimmer = MakeSwimmer(receiver, "Swimmer", "One", 2010);
        db.AddRange(merged, swimmer);
        await db.SaveChangesAsync();

        var resolved = await Repo(db).ResolveClubIdAsync(merged.Id);
        Assert.Equal(receiver.Id, resolved);

        var roster = await Repo(db).GetRosterAsync(resolved!.Value, 1, 50, null, null, null, null);
        Assert.Single(roster.Data);
    }
}
