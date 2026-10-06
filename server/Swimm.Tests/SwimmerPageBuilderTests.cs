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
        string pool = "25m", string? suspect = null, bool timeFail = false,
        bool isMasters = false) => new(
            id, Swimmer, competitionId, DateTime.Parse(date), styleId, distance, "male", pool,
            null, ms, timeFail, suspect, isRelay)
        {
            IsMasters = isMasters,
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
    public void BestTimes_FlaggedSwim_IsNotABest_ButStandsNextToIt()
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
        // …но и не пропадает: стоит рядом с лучшим, со своей пометкой.
        Assert.NotNull(row.UnderReview);
        Assert.Equal(32000, row.UnderReview!.TimeMs);
        Assert.Equal("personal_outlier", row.UnderReview.Quality.Reason);
    }

    [Fact]
    public void BestTimes_DistanceWithOnlyFlaggedSwims_GetsItsOwnRow_AfterTheRealBests()
    {
        // Эталон — пловец 60101: 200 вольным проплыто один раз, и это 1:53.09 из сломанного
        // хронометража. Строка есть (пловец видит заплыв), в конце списка, очки как в протоколе (Р76).
        var rows = new[]
        {
            Row(1, "2025-02-01", distance: "200", ms: 113090, points: 678, suspect: "manual"),
            Row(2, "2025-02-01", distance: "200", ms: 140000, points: 300, suspect: "manual"),
            Row(3, "2025-01-24", distance: "50", ms: 30550, points: 287),
        };

        var best = SwimmerPageBuilder.BestTimes(rows, season: 2024, birthYear: 2012);

        Assert.Equal(["50", "200"], best.Select(b => b.Distance));
        var flagged = best[1];
        Assert.Equal(113090, flagged.TimeMs);              // самый быстрый из помеченных
        Assert.NotNull(flagged.Quality);
        Assert.Equal(678, flagged.Points);                  // напечатано в протоколе — показываем
        Assert.False(flagged.IsCareerBest);
        Assert.Null(flagged.UnderReview);                   // сам себе не сосед
        Assert.Null(best[0].UnderReview);
    }

    [Fact]
    public void Summary_FlaggedSwim_PointsCountAsPrinted()
    {
        var rows = new[]
        {
            Row(1, "2025-02-01", points: 678, suspect: "manual", competitionId: 1),
            Row(2, "2025-02-01", points: 300, competitionId: 1),
        };

        var summary = SwimmerPageBuilder.Summary(rows, season: 2024, NoKinds);

        // Очки протокола — данные, а не наше достижение: в сумме они есть (Р76).
        Assert.Equal(978, summary.Points);
        Assert.Equal(978, summary.Competitions.Single().Points);
        Assert.Equal(2, summary.Swims);                     // заплыв был — в счёт стартов идёт
    }

    [Fact]
    public void UnderReview_ListsEveryFlaggedSwimOfTheCareer_NewestFirst()
    {
        var rows = new[]
        {
            Row(1, "2024-03-01", points: 500, suspect: "time_outlier"),
            Row(2, "2025-02-01", points: 678, suspect: "manual"),
            Row(3, "2025-02-02"),
        };

        var list = SwimmerPageBuilder.UnderReview(rows, birthYear: 2012);

        Assert.Equal([2L, 1L], list.Select(r => r.ResultId));
        Assert.Equal([678, 500], list.Select(r => r.Points));   // очки как в протоколе (Р76)
        Assert.Equal(list.Count, rows.Count(SwimmerPageBuilder.IsUnderReview));
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

    /// <summary>
    /// Срез справочника одной ступени. Ступеней несколько, потому что «свой рекорд» зависит
    /// от ЗАПЛЫВА: мастерский старт меряется полосой возраста, обычный взрослый — открытым
    /// рекордом страны, ребёнок — своей возрастной ступенью.
    /// </summary>
    private static IReadOnlyDictionary<
        SwimmerPageBuilder.RecordStep, IReadOnlyDictionary<string, NationalAgeRecordRow>> Step(
        string category, string ageKey, string key, NationalAgeRecordRow record) =>
        new Dictionary<SwimmerPageBuilder.RecordStep, IReadOnlyDictionary<string, NationalAgeRecordRow>>
        {
            [new SwimmerPageBuilder.RecordStep(category, ageKey)] =
                new Dictionary<string, NationalAgeRecordRow> { [key] = record },
        };

    private static IReadOnlyDictionary<
        SwimmerPageBuilder.RecordStep, IReadOnlyDictionary<string, NationalAgeRecordRow>> NoRecords() =>
        new Dictionary<SwimmerPageBuilder.RecordStep, IReadOnlyDictionary<string, NationalAgeRecordRow>>();

    [Fact]
    public void PersonalBests_DeltasToClubBestAndNationalRecord()
    {
        var rows = new[] { Row(1, "2026-02-16", ms: 59000) };
        var key = SeasonAggregator.DisciplineKey(rows[0]);

        // Заплыв 2026 года у пловца 2014 г.р. (ось календарная) — ступень age/12.
        var pbs = SwimmerPageBuilder.PersonalBests(
            rows, poolType: null,
            clubBestMs: new Dictionary<string, int> { [key] = 58500 },
            nationalRecords: Step("age", "12", key, new("58.00", 58000, "Кто-то", "12")),
            birthYear: 2014);

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
            nationalRecords: Step("age", "12", key, new("58.50", 58500, "Тёзка", "12")),
            birthYear: 2014);

        var pb = Assert.Single(pbs);
        Assert.True(pb.HoldsClubBest);
        Assert.Equal(0, pb.DeltaToClubBestMs);
        Assert.True(pb.HoldsNationalAgeRecord);      // быстрее рекорда — держит его
        Assert.Equal(-500, pb.DeltaToNationalAgeRecordMs);
    }

    [Fact]
    public void PersonalBests_MastersSwim_IsMeasuredByItsBand_NotByChildStep()
    {
        // Мастерс 45 лет: эталон — masters/45-49. С детской ступенью «age/45» дельта была
        // пустой у всех взрослых (поймано 02.09.2026 на пловце 7424).
        var rows = new[] { Row(1, "2026-02-16", ms: 59000, isMasters: true) };
        var key = SeasonAggregator.DisciplineKey(rows[0]);

        var byBand = SwimmerPageBuilder.PersonalBests(
            rows, poolType: null, clubBestMs: new Dictionary<string, int>(),
            nationalRecords: Step("masters", "45-49", key, new("00:60.00", 60000, null, "45-49")),
            birthYear: 1981);

        var pb = Assert.Single(byBand);
        Assert.True(pb.HoldsNationalAgeRecord);
        Assert.Equal("masters 45-49", pb.NationalRecordScope);

        // Тот же рекорд в детской ступени к мастерскому заплыву не относится.
        var byChildStep = SwimmerPageBuilder.PersonalBests(
            rows, poolType: null, clubBestMs: new Dictionary<string, int>(),
            nationalRecords: Step("age", "45", key, new("00:60.00", 60000, null, "45")),
            birthYear: 1981);

        Assert.Null(Assert.Single(byChildStep).NationalRecordScope);
    }

    [Fact]
    public void PersonalBests_Adult_FallsBackToOpenRecord()
    {
        // Взрослый не-мастерс: ступени «age/29» в справочнике нет, эталон — открытый рекорд.
        var rows = new[] { Row(1, "2026-02-16", ms: 59000) };
        var key = SeasonAggregator.DisciplineKey(rows[0]);

        var pbs = SwimmerPageBuilder.PersonalBests(
            rows, poolType: null, clubBestMs: new Dictionary<string, int>(),
            nationalRecords: Step("open", "", key, new("00:58.00", 58000, null, "")),
            birthYear: 1997);

        var pb = Assert.Single(pbs);
        Assert.Equal(1000, pb.DeltaToNationalAgeRecordMs);
        Assert.Equal("open", pb.NationalRecordScope);
    }

    [Fact]
    public void PersonalBests_Child_IsNotComparedWithTheOpenRecord()
    {
        // Девятилетней открытый рекорд страны не эталон: детская лестница справочника
        // начинается с 10, и без отсечки в её личниках появлялась «Δ Israel +19.18».
        var rows = new[] { Row(1, "2026-02-16", ms: 59000) };
        var key = SeasonAggregator.DisciplineKey(rows[0]);

        var pbs = SwimmerPageBuilder.PersonalBests(
            rows, poolType: null, clubBestMs: new Dictionary<string, int>(),
            nationalRecords: Step("open", "", key, new("00:24.46", 24460, null, "")),
            birthYear: 2017);

        var pb = Assert.Single(pbs);
        Assert.Null(pb.NationalRecordScope);
        Assert.Null(pb.DeltaToNationalAgeRecordMs);
    }

    [Fact]
    public void PersonalBests_PoolFilter_KeepsCoursesApart()
    {
        var rows = new[]
        {
            Row(1, "2026-02-16", ms: 59000, pool: "25m"),
            Row(2, "2026-07-16", ms: 61000, pool: "50m"),
        };

        var shortCourse = SwimmerPageBuilder.PersonalBests(rows, "25m", new Dictionary<string, int>(), NoRecords());
        var longCourse = SwimmerPageBuilder.PersonalBests(rows, "50m", new Dictionary<string, int>(), NoRecords());

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
        Assert.Equal(900, flagged.Points);                       // как в протоколе (Р76)
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

    // ── Season best: мастерский заплыв меряется своей группой протокола ─────────

    /// <summary>Лучшее время на мастерском старте — круг задан группой протокола.</summary>
    private static PeerSeasonBest MastersPeer(int swimmerId, int ms, string ageGroup) =>
        Peer(swimmerId, ms) with { MastersAgeGroup = ageGroup };

    [Fact]
    public void SeasonRanks_MastersSwim_RanksInsideProtocolGroup_NotBirthYear()
    {
        // Регрессия 24.09.2026: мастерс 1981 г. р. получал «#1 of 3» среди ровесников, а ссылка
        // строки (срез /season-best с age=45) открывала пустой список — мастерских стартов в
        // нём нет. Место обязано считаться там, куда ведёт ссылка: в группе протокола.
        var rows = new[] { Row(1, "2026-02-16", ms: 60000) with { IsMasters = true, AgeGroup = "45-49" } };
        var peers = new[]
        {
            Peer(8, 50000),                         // ровесник на ОБЫЧНОМ старте — не наш круг
            MastersPeer(Swimmer, 60000, "45-49"),
            MastersPeer(7, 59000, "45-49"),
            MastersPeer(9, 61000, "45-49"),
            MastersPeer(10, 40000, "40-44"),        // соседняя группа — не наш круг
        };

        var dto = SwimmerPageBuilder.SeasonRanks(rows, season: 2025, birthYear: 1981, "female", peers);

        var rank = Assert.Single(dto.Rows);
        Assert.Equal(2, rank.Rank);
        Assert.Equal(3, rank.PeerCount);
        Assert.Equal(59000, rank.LeaderTimeMs);
        Assert.Equal("45-49", rank.AgeGroup);
        Assert.Equal("women 45-49", rank.GroupLabel);
    }

    [Fact]
    public void SeasonRanks_RegularSwim_IgnoresMastersPeers_AndHasNoAgeGroup()
    {
        var rows = new[] { Row(1, "2026-02-16", ms: 60000) };
        var peers = new[]
        {
            Peer(Swimmer, 60000),
            Peer(7, 61000),
            MastersPeer(9, 50000, "25-29"),         // мастерский старт в обычный круг не входит
        };

        var dto = SwimmerPageBuilder.SeasonRanks(rows, season: 2025, birthYear: 2001, "female", peers);

        var rank = Assert.Single(dto.Rows);
        Assert.Equal(1, rank.Rank);
        Assert.Equal(2, rank.PeerCount);
        Assert.Null(rank.AgeGroup);
        Assert.Equal("women 25", rank.GroupLabel);
    }

    [Fact]
    public void SeasonRanks_MastersSwimWithoutGroup_IsSkipped()
    {
        // Без группы протокола мастерскому заплыву не с кем сравниваться — места нет.
        var rows = new[] { Row(1, "2026-02-16", ms: 60000) with { IsMasters = true, AgeGroup = "" } };
        var peers = new[] { Peer(Swimmer, 60000), Peer(7, 61000) };

        var dto = SwimmerPageBuilder.SeasonRanks(rows, season: 2025, birthYear: 1981, "female", peers);

        Assert.Empty(dto.Rows);
    }
}
