using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сборка ответов страницы спортсмена (A2): сезоны для карусели и сводка сезона.
/// Держим то, что легко потерять: умолчание карусели — ВИТРИННЫЙ сезон (до зимних
/// чемпионатов прошлый), дни многодневки — один старт, медали только там, где их вручали,
/// личники считаются по всей карьере, а не заново внутри сезона.
/// </summary>
public class SwimmerPageBuilderTests
{
    private const int Swimmer = 42;

    private static SeasonSwimRow Row(
        long id, string date, int styleId = 1, string distance = "100",
        int? ms = 60000, int? position = null, int points = 0, bool isAward = true,
        int competitionId = 1, int? eventId = null, bool isRelay = false,
        string pool = "25m", string? suspect = null, bool timeFail = false) => new(
            id, Swimmer, competitionId, DateTime.Parse(date), styleId, distance, "male", pool,
            null, ms, timeFail, suspect, isRelay)
        {
            Position = position,
            InternationalPoints = points,
            IsAward = isAward,
            EventId = eventId,
            CompetitionName = $"Meet {competitionId}",
        };

    private static readonly Dictionary<int, string?> NoKinds = [];

    [Fact]
    public void Seasons_DefaultIsTheShowcaseSeason_NotTheFreshestOne()
    {
        // Октябрь нового сезона: заплывы 2026/27 уже есть, но зимние чемпионаты не проплыли,
        // поэтому карусель обязана стоять на 2025/26.
        var rows = new[]
        {
            Row(1, "2026-02-16"),          // сезон 2025
            Row(2, "2026-10-10"),          // сезон 2026
        };

        var seasons = SwimmerPageBuilder.Seasons(rows, showcaseSeason: 2025, currentSeason: 2026);

        Assert.Equal([2026, 2025], seasons.Select(s => s.Season));
        Assert.True(seasons.Single(s => s.Season == 2025).IsDisplayDefault);
        Assert.True(seasons.Single(s => s.Season == 2026).IsCurrent);
        Assert.False(seasons.Single(s => s.Season == 2026).IsDisplayDefault);
    }

    [Fact]
    public void Seasons_ShowcaseSeasonWithoutSwims_FallsBackToTheOlderOne()
    {
        // Пловец пропустил витринный сезон: умолчание съезжает НАЗАД, а не на свежий сезон,
        // который витрина ещё не считает состоявшимся.
        var rows = new[] { Row(1, "2024-02-16"), Row(2, "2026-10-10") };

        var seasons = SwimmerPageBuilder.Seasons(rows, showcaseSeason: 2025, currentSeason: 2026);

        Assert.True(seasons.Single(s => s.Season == 2023).IsDisplayDefault);
    }

    [Fact]
    public void Summary_MultiDayEvent_CountsAsOneCompetition()
    {
        var rows = new[]
        {
            Row(1, "2026-02-24", competitionId: 10, eventId: 7),
            Row(2, "2026-02-25", competitionId: 11, eventId: 7),
            Row(3, "2026-01-10", competitionId: 12),
        };

        var summary = SwimmerPageBuilder.Summary(rows, season: 2025, NoKinds);

        Assert.Equal(2, summary.CompetitionCount);
        Assert.Equal(3, summary.Swims);
        Assert.Equal("2025/26", summary.Label);
    }

    [Fact]
    public void Summary_MedalsCountedOnlyWhereAwarded()
    {
        var rows = new[]
        {
            Row(1, "2026-02-16", position: 1),
            Row(2, "2026-02-17", position: 2),
            Row(3, "2026-02-18", position: 1, isAward: false),   // старт без наград
        };

        var summary = SwimmerPageBuilder.Summary(rows, season: 2025, NoKinds);

        Assert.Equal(1, summary.Medals.Gold);
        Assert.Equal(1, summary.Medals.Silver);
    }

    [Fact]
    public void Summary_RelayMedalCounts_ButNotAsAnEvent()
    {
        var rows = new[]
        {
            Row(1, "2026-02-16", position: 3),
            Row(2, "2026-02-16", distance: "4x100", position: 1, isRelay: true),
        };

        var summary = SwimmerPageBuilder.Summary(rows, season: 2025, NoKinds);

        Assert.Equal(1, summary.Medals.Gold);      // командная награда так же личная
        Assert.Equal(1, summary.Medals.Bronze);
        Assert.Equal(2, summary.Swims);
        Assert.Equal(1, summary.Events);           // но дисциплиной эстафета не считается
    }

    [Fact]
    public void Summary_SuspectAndDsq_AreNotEvents_ButStayInSwims()
    {
        var rows = new[]
        {
            Row(1, "2026-02-16"),
            Row(2, "2026-02-17", distance: "200", suspect: "personal_outlier"),
            Row(3, "2026-02-18", distance: "400", timeFail: true, ms: null),
        };

        var summary = SwimmerPageBuilder.Summary(rows, season: 2025, NoKinds);

        Assert.Equal(3, summary.Swims);     // из протокола строки не исчезают
        Assert.Equal(1, summary.Events);    // но в зачёт дисциплин не идут
    }

    [Fact]
    public void Summary_PersonalBests_CountedAcrossCareer_ShownPerSeason()
    {
        // Один и тот же ключ дисциплины: первый заплыв — личник, второй медленнее — нет,
        // третий быстрее — снова личник.
        var rows = new[]
        {
            Row(1, "2025-02-16", ms: 61000),
            Row(2, "2026-02-16", ms: 62000),
            Row(3, "2026-02-17", ms: 60000),
        };

        var previous = SwimmerPageBuilder.Summary(rows, season: 2024, NoKinds);
        var current = SwimmerPageBuilder.Summary(rows, season: 2025, NoKinds);

        Assert.Equal(1, previous.PersonalBests);
        Assert.Equal(1, current.PersonalBests);   // не 2: медленный заплыв личником не стал
    }

    [Fact]
    public void Summary_AllSeasons_IsCareer()
    {
        var rows = new[] { Row(1, "2025-02-16", points: 100), Row(2, "2026-02-16", points: 200) };

        var career = SwimmerPageBuilder.Summary(rows, season: null, NoKinds);

        Assert.Equal(300, career.Points);
        Assert.Equal(2, career.Swims);
        Assert.Equal("career", career.Label);
    }

    // ── Таб Results: одна дистанция — одна строка (A3) ───────────────────────────

    [Fact]
    public void BestTimes_OneRowPerDistance_WithSeasonBestTime()
    {
        var rows = new[]
        {
            Row(1, "2026-02-16", ms: 61000),
            Row(2, "2026-02-17", ms: 59000),                    // та же дисциплина, быстрее
            Row(3, "2026-02-18", distance: "200", ms: 130000),
        };

        var best = SwimmerPageBuilder.BestTimes(rows, season: 2025, birthYear: 2014);

        Assert.Equal(2, best.Count);
        var hundred = best.Single(b => b.Distance == "100");
        Assert.Equal(59000, hundred.TimeMs);
        Assert.Equal(12, hundred.AgeInSeason);                  // 2026 − 2014, один на сезон
        Assert.True(hundred.IsCareerBest);
    }

    [Fact]
    public void BestTimes_CategoriesOfTheSameDistance_CollapseToOneRow()
    {
        // Маккабиада: «50m Freestyle - Men», «- U17 Boys», «- Men Para» — три зачёта одной
        // дистанции. Медали у них разные, а «моё лучшее на 50 вольным» одно.
        var rows = new[]
        {
            Row(1, "2026-07-10", ms: 30000) with { EventCategory = "open" },
            Row(2, "2026-07-11", ms: 29000) with { EventCategory = "17" },
        };

        var best = SwimmerPageBuilder.BestTimes(rows, season: 2025, birthYear: 2010);

        Assert.Single(best);
        Assert.Equal(29000, best[0].TimeMs);
    }

    [Fact]
    public void BestTimes_FlaggedSwim_IsNotABest_AndHasNoPoints()
    {
        var rows = new[]
        {
            Row(1, "2026-02-16", ms: 61000, points: 400),
            Row(2, "2026-02-17", ms: 32000, points: 900, suspect: "personal_outlier"),
        };

        var best = SwimmerPageBuilder.BestTimes(rows, season: 2025, birthYear: 2014);

        var row = Assert.Single(best);
        Assert.Equal(61000, row.TimeMs);      // помеченное время лучшим не становится
        Assert.Null(row.Quality);
    }

    [Fact]
    public void BestTimes_SeasonBestIsNotAlwaysCareerBest()
    {
        var rows = new[]
        {
            Row(1, "2025-02-16", ms: 58000),   // карьерный личник, прошлый сезон
            Row(2, "2026-02-16", ms: 59000),   // лучшее этого сезона
        };

        var best = SwimmerPageBuilder.BestTimes(rows, season: 2025, birthYear: 2014);

        var row = Assert.Single(best);
        Assert.Equal(59000, row.TimeMs);
        Assert.False(row.IsCareerBest);
    }

    // ── Таб Records & PB: дельты (A3) ────────────────────────────────────────────

    [Fact]
    public void PersonalBests_DeltasToClubBestAndNationalRecord()
    {
        var rows = new[] { Row(1, "2026-02-16", ms: 59000) };
        var key = SeasonAggregator.DisciplineKey(rows[0]);

        var pbs = SwimmerPageBuilder.PersonalBests(
            rows, poolType: null,
            clubBestMs: new Dictionary<string, int> { [key] = 58500 },
            nationalRecords: new Dictionary<string, NationalAgeRecordRow>
            {
                [key] = new("58.00", 58000, "Кто-то", "12"),
            });

        var pb = Assert.Single(pbs);
        Assert.Equal(500, pb.DeltaToClubBestMs);
        Assert.False(pb.HoldsClubBest);
        Assert.Equal(1000, pb.DeltaToNationalAgeRecordMs);
        Assert.False(pb.HoldsNationalAgeRecord);
        Assert.Equal("58.00", pb.NationalAgeRecordTime);
    }

    [Fact]
    public void PersonalBests_HoldingTheBest_IsDecidedByTime_NotByName()
    {
        var rows = new[] { Row(1, "2026-02-16", ms: 58000) };
        var key = SeasonAggregator.DisciplineKey(rows[0]);

        var pbs = SwimmerPageBuilder.PersonalBests(
            rows, poolType: null,
            clubBestMs: new Dictionary<string, int> { [key] = 58000 },   // он же и есть лучший
            nationalRecords: new Dictionary<string, NationalAgeRecordRow>
            {
                [key] = new("58.50", 58500, "Тёзка", "12"),
            });

        var pb = Assert.Single(pbs);
        Assert.True(pb.HoldsClubBest);
        Assert.Equal(0, pb.DeltaToClubBestMs);
        Assert.True(pb.HoldsNationalAgeRecord);      // быстрее рекорда — держит его
        Assert.Equal(-500, pb.DeltaToNationalAgeRecordMs);
    }

    [Fact]
    public void PersonalBests_PoolFilter_KeepsCoursesApart()
    {
        var rows = new[]
        {
            Row(1, "2026-02-16", ms: 59000, pool: "25m"),
            Row(2, "2026-07-16", ms: 61000, pool: "50m"),
        };

        var shortCourse = SwimmerPageBuilder.PersonalBests(rows, "25m", new Dictionary<string, int>(), new Dictionary<string, NationalAgeRecordRow>());
        var longCourse = SwimmerPageBuilder.PersonalBests(rows, "50m", new Dictionary<string, int>(), new Dictionary<string, NationalAgeRecordRow>());

        Assert.Equal(59000, Assert.Single(shortCourse).TimeMs);
        Assert.Equal(61000, Assert.Single(longCourse).TimeMs);
    }

    // ── Таб Progress (A4) ────────────────────────────────────────────────────────

    [Fact]
    public void Progress_OrdersByDate_AndMarksRunningPersonalBests()
    {
        var rows = new[]
        {
            Row(3, "2026-02-18", ms: 58000),
            Row(1, "2025-02-16", ms: 61000),
            Row(2, "2026-02-16", ms: 62000),
            Row(4, "2026-02-19", distance: "200", ms: 130000),   // другая дисциплина
        };
        var key = SeasonAggregator.DisciplineKey(rows[1]);

        var progress = SwimmerPageBuilder.Progress(rows, key, birthYear: 2014);

        Assert.Equal(3, progress.Points.Count);
        Assert.Equal(["2025-02-16", "2026-02-16", "2026-02-18"], progress.Points.Select(p => p.Date));
        Assert.Equal([true, false, true], progress.Points.Select(p => p.IsPb));
        Assert.Equal(11, progress.Points[0].AgeInSeason);        // сезон 2024/25 → 2025 − 2014
        Assert.Equal(12, progress.Points[1].AgeInSeason);
    }

    [Fact]
    public void Progress_FlaggedPoint_StaysInTheList_ButIsNotAPb()
    {
        var rows = new[]
        {
            Row(1, "2026-02-16", ms: 61000),
            Row(2, "2026-02-17", ms: 32000, points: 900, suspect: "personal_outlier"),
        };
        var key = SeasonAggregator.DisciplineKey(rows[0]);

        var progress = SwimmerPageBuilder.Progress(rows, key, birthYear: 2014);

        Assert.Equal(2, progress.Points.Count);
        var flagged = progress.Points[1];
        Assert.False(flagged.IsPb);
        Assert.NotNull(flagged.Quality);
        Assert.Equal("protocol", flagged.Quality!.Kind);
        Assert.Null(flagged.Points);
    }

    [Fact]
    public void Progress_UnknownDiscipline_ReturnsEmptyShape()
    {
        var progress = SwimmerPageBuilder.Progress([Row(1, "2026-02-16")], "нет-такой", birthYear: 2014);

        Assert.Empty(progress.Points);
        Assert.Equal("нет-такой", progress.DisciplineKey);
    }

    // ── Season best: место среди сверстников ─────────────────────────────────

    /// <summary>Лучшее время сверстника в дисциплине; ключ собирается так же, как в репозитории.</summary>
    private static PeerSeasonBest Peer(int swimmerId, int ms, int styleId = 1, string distance = "100") =>
        new(swimmerId, SeasonAggregator.DisciplineKey(styleId, distance, "25m", "male"), ms);

    [Fact]
    public void SeasonRanks_CountsPlaceAmongPeers_AndLabelsTheGroup()
    {
        var rows = new[] { Row(1, "2026-02-16", ms: 60000) };
        var cohort = new[]
        {
            Peer(Swimmer, 60000),
            Peer(7, 58000),      // быстрее — он первый
            Peer(8, 61000),
        };

        var dto = SwimmerPageBuilder.SeasonRanks(rows, season: 2025, birthYear: 2017, "male", cohort);

        var rank = Assert.Single(dto.Rows);
        Assert.Equal(2, rank.Rank);
        Assert.Equal(3, rank.PeerCount);
        Assert.Equal(58000, rank.LeaderTimeMs);
        Assert.Equal(2000, rank.GapToLeaderMs);
        Assert.Equal(9, dto.Age);              // сезон 2025/26 → возраст по 2026
        Assert.Equal("boys 9", dto.GroupLabel);
    }

    [Fact]
    public void SeasonRanks_EqualTimes_ShareThePlace()
    {
        // Спортивный ранжир: двое с одинаковым временем оба вторые, следующий — четвёртый.
        var rows = new[] { Row(1, "2026-02-16", ms: 60000) };
        var cohort = new[] { Peer(Swimmer, 60000), Peer(7, 59000), Peer(8, 60000), Peer(9, 61000) };

        var dto = SwimmerPageBuilder.SeasonRanks(rows, season: 2025, birthYear: 2017, "male", cohort);

        Assert.Equal(2, Assert.Single(dto.Rows).Rank);
    }

    [Fact]
    public void SeasonRanks_FastestInTheGroup_IsFirst()
    {
        var rows = new[] { Row(1, "2026-02-16", ms: 58000) };
        var cohort = new[] { Peer(Swimmer, 58000), Peer(7, 59000) };

        var dto = SwimmerPageBuilder.SeasonRanks(rows, season: 2025, birthYear: 2017, "female", cohort);

        var rank = Assert.Single(dto.Rows);
        Assert.Equal(1, rank.Rank);
        Assert.Equal(0, rank.GapToLeaderMs);
        Assert.Equal("girls 9", dto.GroupLabel);
    }

    [Fact]
    public void SeasonRanks_CareerScope_HasNoPlaces()
    {
        // «Где я среди сверстников» живёт внутри сезона: за карьеру сравнивать не с чем.
        var rows = new[] { Row(1, "2026-02-16") };

        var dto = SwimmerPageBuilder.SeasonRanks(rows, season: null, birthYear: 2017, "male", []);

        Assert.Empty(dto.Rows);
        Assert.Null(dto.Age);
        Assert.Equal("career", dto.Label);
    }

    [Fact]
    public void SeasonRanks_DisciplineMissingFromCohort_IsSkipped_NotDeclaredFirst()
    {
        // Своей строки в когорте нет (год рождения в справочнике разъехался) — молча объявить
        // первое место нельзя: цифра выглядела бы достижением, не будучи им.
        var rows = new[] { Row(1, "2026-02-16", ms: 60000) };

        var dto = SwimmerPageBuilder.SeasonRanks(rows, season: 2025, birthYear: 2017, "male", []);

        Assert.Empty(dto.Rows);
    }

    [Fact]
    public void SeasonRanks_SkipsDsqAndFlaggedSwims_LikeBestTimes()
    {
        // Отбор строк тот же, что у /best-times: DSQ и помеченное время местом не награждаются.
        var rows = new[]
        {
            Row(1, "2026-02-16", ms: 55000, timeFail: true),
            Row(2, "2026-02-16", styleId: 2, ms: 55000, suspect: "personal_outlier"),
            Row(3, "2026-02-16", ms: 60000),
        };
        var cohort = new[] { Peer(Swimmer, 60000), Peer(7, 59000) };

        var dto = SwimmerPageBuilder.SeasonRanks(rows, season: 2025, birthYear: 2017, "male", cohort);

        Assert.Equal(2, Assert.Single(dto.Rows).Rank);
    }

    [Fact]
    public void SeasonRanks_AdultGroup_IsCalledWomen_NotGirls()
    {
        var rows = new[] { Row(1, "2026-02-16", ms: 60000) };
        var cohort = new[] { Peer(Swimmer, 60000) };

        var dto = SwimmerPageBuilder.SeasonRanks(rows, season: 2025, birthYear: 2001, "female", cohort);

        Assert.Equal("women 25", dto.GroupLabel);
    }
}
