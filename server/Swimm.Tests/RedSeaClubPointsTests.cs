using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Клубный зачёт «по заплыву целиком» — схема «אליפות הים האדום» (регламенты Эйлата 2025 и 2026):
/// возрастные категории 3/2/1, общая категория (абсолют по времени, все возрасты) 6/4/2, медали —
/// при ≥ 3 участниках категории, зачёт только в заплыве 1.66 км (в протоколе «1600»).
/// docs/data-integrity.md, И-42; docs/competition-overview-cards.md, Top clubs.
/// </summary>
public class RedSeaClubPointsTests
{
    /// <summary>Правило Красного моря: шкала 3/2/1, вес общей 2, минимум 3, дистанция 1600.</summary>
    private static PointRuleClubs RedSea(int id = 0) => new()
    {
        Id = id,
        Version = "redsea.3pt.2026",
        Scope = "all",
        EffectiveFrom = new DateOnly(2025, 1, 1),
        ManualOnly = true,
        DefaultPoints = 0,
        MaxScoringPlace = 3,
        RelayMultiplier = 2,
        OverallMultiplier = 2,
        MinEntrants = 3,
        Distances = "1600",
        Entries =
        [
            new PointRuleClubsEntry { Place = 1, Points = 3 },
            new PointRuleClubsEntry { Place = 2, Points = 2 },
            new PointRuleClubsEntry { Place = 3, Points = 1 },
        ]
    };

    private static ClubPointsContextRow Row(
        long id, string category, int? position, int? timeMs, string gender = "female",
        string distance = "1600", bool timeFail = false, string? failNote = null) =>
        new(id, $"1|{distance}|{gender}", distance, category, position, timeMs, timeFail, failNote, IsRelay: false);

    [Fact]
    public void LegacyRule_DoesNotNeedContext()
    {
        var legacy = RedSea();
        legacy.OverallMultiplier = null;
        legacy.MinEntrants = null;
        legacy.Distances = null;

        Assert.False(PointRulesClubsScoring.NeedsContext(legacy));
        Assert.False(PointRulesClubsScoring.NeedsContext(null));
        Assert.True(PointRulesClubsScoring.NeedsContext(RedSea()));
    }

    /// <summary>
    /// Женщины 1600 м Эйлата-2026 в миниатюре: абсолют выигрывает 16-17 (6), второе время —
    /// победительница 14-15 (4), третье — взрослая из «כללי» (2). Победительницы категорий
    /// получают ещё и свои 3; категория 18-19 из одной пловчихи не платит ничего.
    /// </summary>
    [Fact]
    public void CategoryPlusOverall_AsInRegulation()
    {
        var points = PointRulesClubsScoring.PointsForSet(RedSea(),
        [
            Row(1, "16-17", 1, 1_590_510),  // קליטצ'וק: 3 + абсолют 1-е 6
            Row(2, "16-17", 2, 1_669_060),  // הירש: 2
            Row(3, "16-17", 3, 1_757_470),  // 1
            Row(4, "14-15", 1, 1_657_510),  // אוגינץ: 3 + абсолют 2-е 4
            Row(5, "14-15", 2, 1_697_810),  // 2
            Row(6, "14-15", 3, 1_755_840),  // 1
            Row(7, "mix-0", 1, 1_660_930),  // שורץ (20+): категории нет, абсолют 3-е 2
            Row(8, "18-19", 1, 1_673_430),  // שהם: категория из одной — 0, в абсолюте 4-я — 0
        ]);

        Assert.Equal(9, points[1]);
        Assert.Equal(2, points[2]);
        Assert.Equal(1, points[3]);
        Assert.Equal(7, points[4]);
        Assert.Equal(2, points[5]);
        Assert.Equal(1, points[6]);
        Assert.Equal(2, points[7]);
        Assert.Equal(0, points[8]);
    }

    [Fact]
    public void OtherDistances_ScoreNothing()
    {
        var points = PointRulesClubsScoring.PointsForSet(RedSea(),
        [
            Row(1, "14-15", 1, 4_785_710, distance: "5000"),
            Row(2, "14-15", 2, 4_869_510, distance: "5000"),
            Row(3, "14-15", 3, 4_876_970, distance: "5000"),
        ]);

        Assert.All(points.Values, p => Assert.Equal(0, p));
    }

    [Fact]
    public void NoShows_AreNotEntrants_DisqualifiedAre()
    {
        // Два финишёра + NS: участников 2 < 3 — категория не платит.
        var withNoShow = PointRulesClubsScoring.PointsForSet(RedSea(),
        [
            Row(1, "12-13", 1, 1_759_110),
            Row(2, "12-13", 2, 1_814_780),
            Row(3, "12-13", null, null, timeFail: true, failNote: "NS"),
        ]);
        Assert.Equal(0, withNoShow[1]);

        // Два финишёра + DQ: участников 3 — категория платит, DQ сам очков не получает.
        var withDq = PointRulesClubsScoring.PointsForSet(RedSea(),
        [
            Row(1, "12-13", 1, 1_759_110),
            Row(2, "12-13", 2, 1_814_780),
            Row(3, "12-13", null, null, timeFail: true, failNote: "DQ"),
        ]);
        Assert.Equal(3 + 6, withDq[1]);
        Assert.Equal(2 + 4, withDq[2]);
        Assert.Equal(0, withDq[3]);
    }

    [Fact]
    public void Overall_IsWithinGender_AndTiesShare()
    {
        var points = PointRulesClubsScoring.PointsForSet(RedSea(),
        [
            Row(1, "16-17", 1, 1_400_000, gender: "male"),
            Row(2, "16-17", 1, 1_400_000, gender: "male"),   // делёж 1-го: обоим 3 + 6
            Row(3, "16-17", 3, 1_500_000, gender: "male"),
            Row(4, "16-17", 1, 1_600_000, gender: "female"), // свой абсолют — первая у женщин
            Row(5, "16-17", 2, 1_700_000, gender: "female"),
            Row(6, "16-17", 3, 1_800_000, gender: "female"),
        ]);

        Assert.Equal(9, points[1]);
        Assert.Equal(9, points[2]);
        Assert.Equal(1 + 2, points[3]);   // абсолют 3-е (два первых делят)
        Assert.Equal(3 + 6, points[4]);
    }

    // ── 2025: протокол без категорий — полосы по году рождения строит правило ─────

    private static ClubPointsContextRow Aged(long id, int age, int? overallPlace, int? timeMs,
        bool timeFail = false, string? failNote = null) =>
        new(id, "1|1600|female", "1600", "12-99", overallPlace, timeMs, timeFail, failNote, IsRelay: false, Age: age);

    /// <summary>
    /// Эйлат-2025, 1600 м женщины (протокол loglig печатает только общее место): победительница
    /// 12-13 — она же абсолют (3 + 6); вторая 12-13 — абсолют 2-е (2 + 4); 18-19 — первая в
    /// своей полосе, но полоса из одного — только абсолют 3-е (2). Протокольное место «1..N»
    /// категорией не считается.
    /// </summary>
    [Fact]
    public void AgeBands_BuildCategoriesFromBirthYear()
    {
        var rule = RedSea();
        rule.AgeBands = "12-13,14-15,16-17,18-19";

        var points = PointRulesClubsScoring.PointsForSet(rule,
        [
            Aged(1, 13, 1, 1_423_000),   // אוגינץ: 12-13 1-я + абсолют 1-я
            Aged(2, 13, 2, 1_442_000),   // גורבנקו: 12-13 2-я + абсолют 2-я
            Aged(3, 18, 3, 1_462_000),   // רון: 18-19 одна — 0 + абсолют 3-я
            Aged(4, 13, 4, 1_467_000),   // קאניוק: 12-13 3-я
            Aged(5, 15, 6, 1_472_000),   // ראובני: 14-15 1-я
            Aged(6, 15, 7, 1_474_000),   // 14-15 2-я
            Aged(7, 14, 12, 1_521_000),  // 14-15 3-я
            Aged(8, 25, 5, 1_470_000),   // взрослая: полосы нет, в абсолюте 5-я — 0
            Aged(9, 13, null, null, timeFail: true, failNote: "NS"),
        ]);

        Assert.Equal(3 + 6, points[1]);
        Assert.Equal(2 + 4, points[2]);
        Assert.Equal(0 + 2, points[3]);
        Assert.Equal(1, points[4]);
        Assert.Equal(3, points[5]);
        Assert.Equal(2, points[6]);
        Assert.Equal(1, points[7]);
        Assert.Equal(0, points[8]);
        Assert.Equal(0, points[9]);
    }

    [Theory]
    [InlineData("12-13,14-15", 2)]
    [InlineData("17", 1)]
    public void ParseAgeBands_Valid(string text, int count) =>
        Assert.Equal(count, PointRulesClubsScoring.ParseAgeBands(text)!.Count);

    [Fact]
    public void AdminValidation_RejectsBadAgeBands()
    {
        var input = new Swimm.Application.Dtos.PointRuleInputDto { Version = "redsea.test", Scope = "all", AgeBands = "12-13,abc" };
        var error = Swimm.Infrastructure.Repositories.PointRulesAdminRepository.Validate(
            input, Swimm.Application.Dtos.PointRuleKind.Clubs);
        Assert.StartsWith("Возрастные категории", error);
    }

    [Theory]
    [InlineData(0, null, "Вес общей категории")]
    [InlineData(null, 0, "Минимум участников")]
    public void AdminValidation_RejectsNonPositiveOptions(int? overall, int? minEntrants, string expected)
    {
        var input = new Swimm.Application.Dtos.PointRuleInputDto
        {
            Version = "redsea.test", Scope = "all", OverallMultiplier = overall, MinEntrants = minEntrants,
        };

        var error = Swimm.Infrastructure.Repositories.PointRulesAdminRepository.Validate(
            input, Swimm.Application.Dtos.PointRuleKind.Clubs);

        Assert.StartsWith(expected, error);
    }

    // ── Материализованный зачёт: правило привязано к соревнованию ─────────────

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Fact]
    public async Task Standing_UsesWholeRace_AndIgnoresOtherDistances()
    {
        await using var db = CreateDb(nameof(Standing_UsesWholeRace_AndIgnoresOtherDistances));
        var country = new Country { CountryCode = "ISR", CountryName = "Israel" };
        var batYam = new Club { Name = "הפועל בת ים", NameEn = "", Country = country };
        var bialik = new Club { Name = "מכבי קרית ביאליק", NameEn = "", Country = country };
        var rule = RedSea();
        db.AddRange(country, batYam, bialik, rule, new Style { Id = 100, Name = "freestyle" });
        await db.SaveChangesAsync();

        var comp = new Competition { Name = "Eilat", Date = "25/04/2026", PoolType = "25m", PointRuleClubsId = rule.Id };
        db.Add(comp);
        for (var id = 1; id <= 6; id++)
            db.Add(new Swimmer { Id = id, LastName = $"S{id}", FirstName = "A", BirthYear = 2010 });
        await db.SaveChangesAsync();

        ResultRecord Swim(Club club, int swimmer, string category, int? place, int ms, string distance = "1600") => new()
        {
            CompetitionId = comp.Id, ClubId = club.Id, SwimmerId = swimmer, StyleId = 100,
            CompetitionDate = new DateTime(2026, 4, 25), Distance = distance, Gender = "female",
            EventCategory = category, Position = place, TimeMillisecond = ms,
        };

        db.AddRange(
            Swim(batYam, 1, "16-17", 1, 1_590_510),   // 3 + 6
            Swim(bialik, 2, "16-17", 2, 1_669_060),   // 2 + 4 (абсолют 2-е)
            Swim(bialik, 3, "16-17", 3, 1_757_470),   // 1 + 2 (абсолют 3-е)
            Swim(batYam, 4, "16-17", 1, 4_785_710, distance: "5000"),   // не 1600 — 0
            Swim(bialik, 5, "16-17", 2, 4_869_510, distance: "5000"),
            Swim(bialik, 6, "16-17", 3, 4_876_970, distance: "5000"));
        await db.SaveChangesAsync();

        await new ClubStandingService(db).RebuildForCompetitionAsync(comp.Id);

        var table = await db.ClubCompetitionStandings.ToDictionaryAsync(s => s.ClubId, s => s.Points);
        Assert.Equal(9, table[batYam.Id]);
        Assert.Equal(9, table[bialik.Id]);
    }
}
