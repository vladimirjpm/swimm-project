using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Таб H2H: сравнение двух пловцов бок о бок (<see cref="SwimmerPageBuilder.Compare"/>).
/// Держим то, что легко потерять при правках: ключ строки БЕЗ пола (иначе разнополая пара
/// не совпала бы ни одной дистанцией), бассейны ВНУТРИ строки отдельными парами времён
/// (25м и 50м несравнимы), знак разрыва «моё минус соперника», строки с парой выше
/// односторонних и тот же отбор заплывов, что у остальных табов (без эстафет, помеченных
/// и DSQ).
/// </summary>
public class SwimmerCompareTests
{
    private static SeasonSwimRow Row(
        long id, int swimmerId, string date, string gender,
        int styleId = 1, string distance = "100", string pool = "25m",
        int? ms = 60000, int points = 0, bool isRelay = false,
        string? suspect = null, bool timeFail = false,
        int? position = null, bool isAward = false, bool isMasters = false) => new(
            id, swimmerId, 1, DateTime.Parse(date), styleId, distance, gender, pool,
            null, ms, timeFail, suspect, isRelay)
        {
            InternationalPoints = points,
            CompetitionName = "Meet 1",
            Position = position,
            IsAward = isAward,
            IsMasters = isMasters,
        };

    private static SwimmerProfileDto Profile(
        int id, string name, int birthYear, string gender, int recordsHeld = 0) => new()
    {
        Id = id,
        FullName = name,
        BirthYear = birthYear,
        Gender = gender,
        RecordsHeld = recordsHeld,
    };

    /// <summary>Сторона сравнения без когорты и справочника — их проверяют отдельные тесты.</summary>
    private static SwimmerPageBuilder.SwimmerCompareInput Input(
        SeasonSwimRow[] rows, SwimmerProfileDto profile,
        IReadOnlyList<PeerSeasonBest>? cohort = null,
        IReadOnlyDictionary<
            SwimmerPageBuilder.RecordStep, IReadOnlyDictionary<string, NationalAgeRecordRow>>? records = null) =>
        new(rows, profile, cohort ?? [],
            records ?? new Dictionary<
                SwimmerPageBuilder.RecordStep, IReadOnlyDictionary<string, NationalAgeRecordRow>>());

    /// <summary>Срез справочника одной ступени: («age»,«14») и так далее.</summary>
    private static IReadOnlyDictionary<
        SwimmerPageBuilder.RecordStep, IReadOnlyDictionary<string, NationalAgeRecordRow>> Step(
        string category, string ageKey, string disciplineKey, int timeMs) =>
        new Dictionary<SwimmerPageBuilder.RecordStep, IReadOnlyDictionary<string, NationalAgeRecordRow>>
        {
            [new SwimmerPageBuilder.RecordStep(category, ageKey)] =
                new Dictionary<string, NationalAgeRecordRow>
                {
                    [disciplineKey] = new("00:60.00", timeMs, Holder: null, AgeKey: ageKey),
                },
        };

    [Fact]
    public void Compare_DifferentGenders_StillPairsTheSameDistance()
    {
        // Ключ дисциплины остальных табов включает пол — если бы Compare брал его, у этой
        // пары не совпало бы НИ ОДНОЙ строки при полных данных с обеих сторон.
        var mine = new[] { Row(1, 10, "2026-02-01", "male", ms: 61000) };
        var rival = new[] { Row(2, 20, "2026-02-01", "female", ms: 60000) };

        var dto = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male")),
            Input(rival, Profile(20, "Rival", 2012, "female")),
            season: 2025);

        var row = Assert.Single(dto.Rows);
        var pool = Assert.Single(row.Pools);
        Assert.NotNull(pool.Mine);
        Assert.NotNull(pool.Rival);
        Assert.Equal(1, dto.SharedCount);
    }

    [Fact]
    public void Compare_DeltaIsMineMinusRival_AndCountsFasterSides()
    {
        var mine = new[]
        {
            Row(1, 10, "2026-02-01", "male", styleId: 1, ms: 59000),   // я быстрее на секунду
            Row(2, 10, "2026-02-01", "male", styleId: 2, ms: 30000),   // соперник быстрее
            Row(3, 10, "2026-02-01", "male", styleId: 3, ms: 45000),   // поровну
        };
        var rival = new[]
        {
            Row(4, 20, "2026-02-01", "male", styleId: 1, ms: 60000),
            Row(5, 20, "2026-02-01", "male", styleId: 2, ms: 29000),
            Row(6, 20, "2026-02-01", "male", styleId: 3, ms: 45000),
        };

        var dto = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male")),
            Input(rival, Profile(20, "Rival", 2012, "male")),
            season: 2025);

        Assert.Equal(-1000, dto.Rows.Single(r => r.StyleId == 1).Pools.Single().DeltaMs);
        Assert.Equal(1000, dto.Rows.Single(r => r.StyleId == 2).Pools.Single().DeltaMs);
        Assert.Equal(0, dto.Rows.Single(r => r.StyleId == 3).Pools.Single().DeltaMs);
        Assert.Equal(1, dto.MineFaster);
        Assert.Equal(1, dto.RivalFaster);
        Assert.Equal(1, dto.Ties);
        Assert.Equal(3, dto.SharedCount);
    }

    [Fact]
    public void Compare_SharedDistancesComeFirst_OneSidedHaveNoDelta()
    {
        var mine = new[]
        {
            Row(1, 10, "2026-02-01", "male", styleId: 1, ms: 60000, points: 500),
            Row(2, 10, "2026-02-01", "male", styleId: 4, ms: 30000, points: 900),  // только моя
        };
        var rival = new[]
        {
            Row(3, 20, "2026-02-01", "male", styleId: 1, ms: 61000, points: 480),
            Row(4, 20, "2026-02-01", "male", styleId: 5, ms: 28000, points: 950),  // только его
        };

        var dto = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male")),
            Input(rival, Profile(20, "Rival", 2012, "male")),
            season: 2025);

        // Общая дистанция первая, хотя очки у односторонних выше: сравнивать есть что только там.
        Assert.Equal(1, dto.Rows[0].StyleId);
        Assert.Equal(1, dto.SharedCount);

        var mineOnly = dto.Rows.Single(r => r.StyleId == 4).Pools.Single();
        Assert.Null(mineOnly.DeltaMs);
        Assert.NotNull(mineOnly.Mine);
        Assert.Null(mineOnly.Rival);

        var rivalOnly = dto.Rows.Single(r => r.StyleId == 5).Pools.Single();
        Assert.Null(rivalOnly.Mine);
        Assert.NotNull(rivalOnly.Rival);
    }

    [Fact]
    public void Compare_PoolsLiveInsideOneRow_ButAreComparedSeparately()
    {
        // «100 вольным» — ОДНА дистанция (одна строка), но 25м и 50м сравниваются порознь:
        // время из короткой воды быстрее по устройству бассейна, и общая пара врала бы
        // разрывом. Поэтому счёт считается по бассейнам: здесь 1–1, а не 1–0.
        var mine = new[]
        {
            Row(1, 10, "2026-02-01", "male", pool: "25m", ms: 59000),
            Row(2, 10, "2026-02-01", "male", pool: "50m", ms: 62000),
        };
        var rival = new[]
        {
            Row(3, 20, "2026-02-01", "male", pool: "25m", ms: 60000),
            Row(4, 20, "2026-02-01", "male", pool: "50m", ms: 61000),
        };

        var dto = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male")),
            Input(rival, Profile(20, "Rival", 2012, "male")),
            season: 2025);

        var row = Assert.Single(dto.Rows);
        Assert.Equal(2, row.Pools.Count);
        // Короткая вода первой — строка открывается бассейном, где чаще есть время у обоих.
        Assert.Equal("25m", row.Pools[0].PoolType);
        Assert.Equal(-1000, row.Pools[0].DeltaMs);
        Assert.Equal(1000, row.Pools[1].DeltaMs);

        Assert.Equal(2, dto.SharedCount);
        Assert.Equal(1, dto.MineFaster);
        Assert.Equal(1, dto.RivalFaster);
    }

    [Fact]
    public void Compare_OneSidedPool_KeepsTheRowButHasNoDelta()
    {
        // Я плавал только 25м, соперник — только 50м: строка одна («50 вольным»), но пары
        // в ней две, и ни в одной сравнивать не с чем.
        var mine = new[] { Row(1, 10, "2026-02-01", "male", pool: "25m", ms: 60000) };
        var rival = new[] { Row(2, 20, "2026-02-01", "male", pool: "50m", ms: 60000) };

        var dto = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male")),
            Input(rival, Profile(20, "Rival", 2012, "male")),
            season: 2025);

        var row = Assert.Single(dto.Rows);
        Assert.Equal(2, row.Pools.Count);
        Assert.All(row.Pools, p => Assert.Null(p.DeltaMs));
        Assert.Equal(0, dto.SharedCount);
    }

    [Fact]
    public void Compare_SkipsRelaysSuspectAndFailedSwims()
    {
        var mine = new[]
        {
            Row(1, 10, "2026-02-01", "male", styleId: 1, ms: 59000, isRelay: true),
            Row(2, 10, "2026-02-01", "male", styleId: 2, ms: 59000, suspect: "personal_outlier"),
            Row(3, 10, "2026-02-01", "male", styleId: 3, ms: null, timeFail: true),
            Row(4, 10, "2026-02-01", "male", styleId: 4, ms: 59000),
        };
        var rival = new[]
        {
            Row(5, 20, "2026-02-01", "male", styleId: 1, ms: 60000),
            Row(6, 20, "2026-02-01", "male", styleId: 4, ms: 60000),
        };

        var dto = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male")),
            Input(rival, Profile(20, "Rival", 2012, "male")),
            season: 2025);

        // Эстафета соперника осталась бы «его» строкой без пары, помеченное и DSQ — вовсе нет.
        Assert.Equal(1, dto.SharedCount);
        Assert.Equal(4, dto.Rows.Single(r => r.Pools.Any(p => p.DeltaMs is not null)).StyleId);
        Assert.DoesNotContain(dto.Rows, r => r.StyleId == 2 || r.StyleId == 3);
    }

    [Fact]
    public void Compare_TakesOnlyTheSelectedSeason_AndCareerWhenNull()
    {
        var mine = new[]
        {
            Row(1, 10, "2026-02-01", "male", styleId: 1, ms: 60000),   // сезон 2025/26
            Row(2, 10, "2025-02-01", "male", styleId: 2, ms: 30000),   // сезон 2024/25
        };
        var rival = new[]
        {
            Row(3, 20, "2026-02-01", "male", styleId: 1, ms: 61000),
            Row(4, 20, "2025-02-01", "male", styleId: 2, ms: 31000),
        };

        var season = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male")),
            Input(rival, Profile(20, "Rival", 2012, "male")),
            season: 2025);
        Assert.Equal("2025/26", season.Label);
        Assert.Single(season.Rows);

        var career = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male")),
            Input(rival, Profile(20, "Rival", 2012, "male")),
            season: null);
        Assert.Equal("career", career.Label);
        Assert.Equal(2, career.Rows.Count);
        // Возраст в сезоне за карьеру не определён — «14 лет» относилось бы к одному старту.
        Assert.Null(career.Mine.AgeInSeason);
    }

    [Fact]
    public void Compare_SeasonBestBadge_NeedsTwoPeersAndFirstPlace()
    {
        // Когорта: я 59.0, сверстник 60.0 на стиле 1 → бейдж SB мой. На стиле 2 я один в
        // группе — «первый среди одного» бейджа не даёт (порог MIN_PEERS_FOR_RANK).
        var mine = new[]
        {
            Row(1, 10, "2026-02-01", "male", styleId: 1, ms: 59000),
            Row(2, 10, "2026-02-01", "male", styleId: 2, ms: 30000),
        };
        var rival = new[] { Row(3, 20, "2026-02-01", "male", styleId: 1, ms: 61000) };

        var key1 = SeasonAggregator.DisciplineKey(1, "100", "25m", "male");
        var key2 = SeasonAggregator.DisciplineKey(2, "100", "25m", "male");
        var cohort = new List<PeerSeasonBest>
        {
            new(10, key1, 59000),
            new(99, key1, 60000),
            new(10, key2, 30000),
        };

        var dto = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male"), cohort),
            Input(rival, Profile(20, "Rival", 2012, "male")),
            season: 2025);

        Assert.True(dto.Rows.Single(r => r.StyleId == 1).Pools.Single().Mine!.IsSeasonBest);
        Assert.False(dto.Rows.Single(r => r.StyleId == 2).Pools.Single().Mine!.IsSeasonBest);
        Assert.Equal(1, dto.Mine.SeasonBests);
        Assert.Equal(0, dto.Rival.SeasonBests);
    }

    [Fact]
    public void Compare_SeasonBestBadge_ForCareerIsCountedForShowcaseSeason()
    {
        // Режим ∞: места среди сверстников живут внутри сезона, но прятать их нельзя —
        // считаем за ВИТРИННЫЙ сезон (как фильтр Season best) и говорим это подписью.
        var mine = new[] { Row(1, 10, "2026-02-01", "male", ms: 59000) };
        var rival = new[] { Row(2, 20, "2026-02-01", "male", ms: 61000) };
        var key = SeasonAggregator.DisciplineKey(1, "100", "25m", "male");
        var cohort = new List<PeerSeasonBest> { new(10, key, 59000), new(99, key, 60000) };

        var dto = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male"), cohort),
            Input(rival, Profile(20, "Rival", 2012, "male")),
            season: null, seasonBestSeason: 2025);

        Assert.True(dto.Rows.Single().Pools.Single().Mine!.IsSeasonBest);
        Assert.Equal(1, dto.Mine.SeasonBests);
        Assert.Equal(2025, dto.SeasonBestSeason);
        Assert.Equal("2025/26", dto.SeasonBestLabel);
    }

    [Fact]
    public void Compare_SeasonBestBadge_NotOnACareerBestFromAnotherSeason()
    {
        // Карьерное лучшее из ПРОШЛОГО сезона: место посчитано за витринный, и бейдж на
        // чужом заплыве обещал бы первое место времени, которого в том сезоне не было.
        var mine = new[]
        {
            Row(1, 10, "2025-02-01", "male", ms: 55000),   // карьерное лучшее, сезон 2024/25
            Row(2, 10, "2026-02-01", "male", ms: 59000),   // лучшее витринного сезона
        };
        var rival = new[] { Row(3, 20, "2026-02-01", "male", ms: 61000) };
        var key = SeasonAggregator.DisciplineKey(1, "100", "25m", "male");
        var cohort = new List<PeerSeasonBest> { new(10, key, 59000), new(99, key, 60000) };

        var dto = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male"), cohort),
            Input(rival, Profile(20, "Rival", 2012, "male")),
            season: null, seasonBestSeason: 2025);

        // Показано карьерное 55.00 — бейджа на нём нет, хотя счётчик стороны место засчитал.
        Assert.False(dto.Rows.Single().Pools.Single().Mine!.IsSeasonBest);
        Assert.Equal(1, dto.Mine.SeasonBests);
    }

    [Fact]
    public void Compare_RecordBadge_IsDecidedByTimeNotByName()
    {
        // Рекорд ступени 60.00: моё 59.0 не медленнее → REC; время соперника 61.0 — нет.
        // Ось календарная (дефолт): заплыв 2026 года, 2012 г.р. → ступень age/14.
        var mine = new[] { Row(1, 10, "2026-02-01", "male", ms: 59000) };
        var rival = new[] { Row(2, 20, "2026-02-01", "male", ms: 61000) };
        var key = SeasonAggregator.DisciplineKey(1, "100", "25m", "male");
        var records = Step("age", "14", key, 60000);

        var dto = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male"), records: records),
            Input(rival, Profile(20, "Rival", 2012, "male"), records: records),
            season: 2025);

        var pool = dto.Rows.Single().Pools.Single();
        Assert.True(pool.Mine!.HoldsRecord);
        Assert.False(pool.Rival!.HoldsRecord);
    }

    [Fact]
    public void Compare_RecordBadge_MastersSwimIsMeasuredByItsBand()
    {
        // Мастерс 45 лет: его рекорд лежит в masters/45-49, а НЕ в age/45 — с числовым
        // ключом бейдж не находился никогда (баг, пойманный на паре 7424 × 62098).
        var mine = new[] { Row(1, 10, "2026-02-01", "female", ms: 59000, isMasters: true) };
        var rival = new[] { Row(2, 20, "2026-02-01", "female", ms: 61000, isMasters: true) };
        var key = SeasonAggregator.DisciplineKey(1, "100", "25m", "female");

        var band = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 1981, "female"), records: Step("masters", "45-49", key, 60000)),
            Input(rival, Profile(20, "Rival", 1981, "female")),
            season: 2025);
        Assert.True(band.Rows.Single().Pools.Single().Mine!.HoldsRecord);

        // Тот же рекорд, положенный в детскую ступень, к мастерскому заплыву не относится.
        var childStep = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 1981, "female"), records: Step("age", "45", key, 60000)),
            Input(rival, Profile(20, "Rival", 1981, "female")),
            season: 2025);
        Assert.False(childStep.Rows.Single().Pools.Single().Mine!.HoldsRecord);
    }

    [Fact]
    public void Compare_RecordBadge_AdultUsesOpenRecord()
    {
        // Взрослый не-мастерс: детской ступени age/29 в справочнике нет, зато есть открытый
        // рекорд страны — по нему бейдж и считается.
        var mine = new[] { Row(1, 10, "2026-02-01", "male", ms: 59000) };
        var rival = new[] { Row(2, 20, "2026-02-01", "male", ms: 61000) };
        var key = SeasonAggregator.DisciplineKey(1, "100", "25m", "male");

        var dto = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 1997, "male"), records: Step("open", "", key, 60000)),
            Input(rival, Profile(20, "Rival", 1997, "male")),
            season: 2025);

        Assert.True(dto.Rows.Single().Pools.Single().Mine!.HoldsRecord);
    }

    [Fact]
    public void Compare_SideStats_CountMedalsAndBestPointsOfThePeriod()
    {
        // Медали только там, где их вручали (IsAward), очки — лучшие за ОДИН заплыв периода.
        var mine = new[]
        {
            Row(1, 10, "2026-02-01", "male", styleId: 1, ms: 59000, points: 500, position: 1, isAward: true),
            Row(2, 10, "2026-02-01", "male", styleId: 2, ms: 30000, points: 620, position: 3, isAward: true),
            Row(3, 10, "2026-02-01", "male", styleId: 3, ms: 45000, points: 700, position: 1, isAward: false),
            Row(4, 10, "2025-02-01", "male", styleId: 4, ms: 20000, points: 900, position: 1, isAward: true),
        };
        var rival = new[] { Row(5, 20, "2026-02-01", "male", styleId: 1, ms: 61000, points: 480) };

        var dto = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male")),
            Input(rival, Profile(20, "Rival", 2012, "male")),
            season: 2025);

        // Золото одно: заплыв без вручения медалей (IsAward = false) в счёт не идёт,
        // прошлогодний — вне периода.
        Assert.Equal(1, dto.Mine.Medals.Gold);
        Assert.Equal(1, dto.Mine.Medals.Bronze);
        Assert.Equal(700, dto.Mine.BestPoints);
        Assert.Equal(480, dto.Rival.BestPoints);
    }

    [Fact]
    public void Compare_SideCarriesRecordsHeld_FromProfileNotFromPeriod()
    {
        // Рекорды приходят из профиля (справочник, матч по имени держателя) и НЕ зависят от
        // выбранного периода: у записи справочника нет сезона.
        var mine = new[] { Row(1, 10, "2026-02-01", "male", ms: 59000) };
        var rival = new[] { Row(2, 20, "2026-02-01", "male", ms: 61000) };

        var season = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male", recordsHeld: 15)),
            Input(rival, Profile(20, "Rival", 2012, "male", recordsHeld: 5)),
            season: 2025);
        Assert.Equal(15, season.Mine.RecordsHeld);
        Assert.Equal(5, season.Rival.RecordsHeld);

        var career = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male", recordsHeld: 15)),
            Input(rival, Profile(20, "Rival", 2012, "male", recordsHeld: 5)),
            season: null);
        Assert.Equal(15, career.Mine.RecordsHeld);
    }

    [Fact]
    public void Compare_SideCarriesAgeInSeason()
    {
        var mine = new[] { Row(1, 10, "2026-02-01", "male") };
        var rival = new[] { Row(2, 20, "2026-02-01", "female") };

        var dto = SwimmerPageBuilder.Compare(
            Input(mine, Profile(10, "Mine", 2012, "male")),
            Input(rival, Profile(20, "Rival", 2014, "female")),
            season: 2025);

        // Возраст — по году ОКОНЧАНИЯ сезона (2025/26 → 2026), один на все старты сезона.
        Assert.Equal(14, dto.Mine.AgeInSeason);
        Assert.Equal(12, dto.Rival.AgeInSeason);
    }
}
