using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// История клубов пловца (<see cref="SwimmerClubHistory"/>, план swimmer-club-change Ф2–Ф3).
/// Держим то, что легко потерять: клуб — из строки, а не карточки; псевдоклуб и эстафета не
/// клуб; склеенный клуб — это его приёмник (не «смена клуба»); новые сезоны сверху; клуб
/// сезона — клуб ПОСЛЕДНЕГО старта в нём; сторона H2H при выбранном сезоне берёт его.
/// </summary>
public class SwimmerClubHistoryTests
{
    private const int Dolphin = 438;
    private const int Raanana = 449;
    private const int Israel = 900;      // псевдоклуб — сборная
    private const int DolphinOld = 501;  // дубль Дельфина, склеен в 438

    private static readonly IReadOnlyDictionary<int, SwimmerClubRef> Clubs = new Dictionary<int, SwimmerClubRef>
    {
        [Dolphin] = new(Dolphin, "הפועל דולפין נתניה", "Hapoel Dolphin Netanya", false),
        [Raanana] = new(Raanana, "ע.ל.ה עצמה רעננה", "", false),
        [Israel] = new(Israel, "Israel", "Israel", true),
        [DolphinOld] = new(DolphinOld, "דולפין נתניה", "Dolphin", false, MergedIntoId: Dolphin),
    };

    private static SeasonSwimRow Row(
        long id, string date, int clubId, string pool = "25m", bool isRelay = false) => new(
            id, 6073, 1, DateTime.Parse(date), 1, "100", "female", pool, null, 60000, false, null, isRelay)
        {
            ClubId = clubId,
        };

    /// <summary>Пловчиха 6073: два сезона за Дельфин, нынешний — за Раанану.</summary>
    private static SeasonSwimRow[] Viktoria() =>
    [
        Row(1, "2024-12-10", Dolphin),
        Row(2, "2025-07-20", Dolphin, pool: "50m"),
        Row(3, "2025-11-15", Dolphin),
        Row(4, "2026-03-01", Dolphin),
        Row(5, "2026-11-05", Raanana),
        Row(6, "2026-11-06", Raanana),
    ];

    [Fact]
    public void Build_OneRowPerSeasonAndClub_NewestSeasonFirst()
    {
        var history = SwimmerClubHistory.Build(Viktoria(), Clubs);

        Assert.Equal(
            [(2026, Raanana), (2025, Dolphin), (2024, Dolphin)],
            history.Select(h => (h.Season, h.ClubId)));
        var now = history[0];
        Assert.Equal("2026/27", now.SeasonLabel);
        Assert.Equal(2, now.Swims);
        Assert.Equal("2026-11-05", now.FirstDate);
        Assert.Equal("2026-11-06", now.LastDate);
        Assert.Equal(2, SwimmerClubHistory.ClubCount(history));
    }

    [Fact]
    public void Build_KeepsHebrewName_AndDropsEmptyOrSameEnglish()
    {
        var history = SwimmerClubHistory.Build(Viktoria(), Clubs);

        Assert.Null(history[0].NameEn);                      // у Раананы EN пуст
        Assert.Equal("Hapoel Dolphin Netanya", history[1].NameEn);
        Assert.Equal("ע.ל.ה עצמה רעננה", SwimmerClubHistory.DisplayName(history[0]));
    }

    [Fact]
    public void Build_SkipsPseudoClubsAndRelays()
    {
        var rows = new[]
        {
            Row(1, "2026-02-01", Dolphin),
            Row(2, "2026-02-02", Israel),                  // сборная — не клуб
            Row(3, "2026-02-03", Raanana, isRelay: true),  // эстафета — не считается
        };

        var history = SwimmerClubHistory.Build(rows, Clubs);

        Assert.Equal(Dolphin, Assert.Single(history).ClubId);
        Assert.Equal(1, SwimmerClubHistory.ClubCount(history));
    }

    [Fact]
    public void Build_MergedClubCountsAsItsReceiver()
    {
        // Два имени одного клуба — не смена клуба: значка «несколько клубов» быть не должно (К4).
        var rows = new[] { Row(1, "2025-10-01", DolphinOld), Row(2, "2026-02-01", Dolphin) };

        var history = SwimmerClubHistory.Build(rows, Clubs);

        var season = Assert.Single(history);
        Assert.Equal(Dolphin, season.ClubId);
        Assert.Equal(2, season.Swims);
    }

    [Fact]
    public void Resolve_FollowsMergeChain_AndStopsOnLoop()
    {
        var clubs = new Dictionary<int, SwimmerClubRef>
        {
            [1] = new(1, "A", "", false, MergedIntoId: 2),
            [2] = new(2, "B", "", false, MergedIntoId: 3),
            [3] = new(3, "C", "", false),
            [7] = new(7, "X", "", false, MergedIntoId: 8),
            [8] = new(8, "Y", "", false, MergedIntoId: 7),
        };

        Assert.Equal(3, SwimmerClubHistory.Resolve(1, clubs)!.Id);
        Assert.Null(SwimmerClubHistory.Resolve(7, clubs));   // петля — данных нет, а не зависание
        Assert.Null(SwimmerClubHistory.Resolve(99, clubs));  // неизвестный id
    }

    [Fact]
    public void Build_TwoClubsInOneSeason_BothRows_LatestFirst_AndCourses()
    {
        // Зимой за Дельфин (25м), летом — за Раанану (50м): обе строки сезона, значки ❄️ и ☀️.
        var rows = new[]
        {
            Row(1, "2025-12-01", Dolphin, pool: "25m"),
            Row(2, "2026-07-10", Raanana, pool: "50m"),
        };

        var history = SwimmerClubHistory.Build(rows, Clubs);

        Assert.Equal([Raanana, Dolphin], history.Select(h => h.ClubId));
        Assert.Equal(["summer"], history[0].Courses);
        Assert.Equal(["winter"], history[1].Courses);
        Assert.Equal(Raanana, SwimmerClubHistory.ClubOfSeason(history, 2025)!.ClubId);
    }

    [Fact]
    public void ClubOfSeason_IsTheClubOfTheLastStart_NullWithoutStarts()
    {
        var history = SwimmerClubHistory.Build(Viktoria(), Clubs);

        Assert.Equal(Dolphin, SwimmerClubHistory.ClubOfSeason(history, 2025)!.ClubId);
        Assert.Equal(Raanana, SwimmerClubHistory.ClubOfSeason(history, 2026)!.ClubId);
        Assert.Null(SwimmerClubHistory.ClubOfSeason(history, 2019));
    }

    // ── Сторона H2H (Ф3) ────────────────────────────────────────────────────────

    private static SwimmerPageBuilder.SwimmerCompareInput Side(
        int id, SeasonSwimRow[] rows, IReadOnlyList<SwimmerClubSeasonDto>? history) =>
        new(rows,
            new SwimmerProfileDto
            {
                Id = id, FullName = $"#{id}", BirthYear = 2013, Gender = "female",
                // Карточка — ТЕКУЩИЙ клуб (после Ф1 — Раанана).
                ClubId = Raanana, ClubName = "ע.ל.ה עצמה רעננה",
            },
            [],
            new Dictionary<SwimmerPageBuilder.RecordStep, IReadOnlyDictionary<string, NationalAgeRecordRow>>(),
            [],
            history);

    [Fact]
    public void Compare_SideClub_FollowsTheSelectedSeason()
    {
        var rows = Viktoria();
        var history = SwimmerClubHistory.Build(rows, Clubs);
        var rival = Side(20, [Row(9, "2026-02-01", Raanana) with { SwimmerId = 20 }], null);

        var past = SwimmerPageBuilder.Compare(Side(6073, rows, history), rival, season: 2025);
        var now = SwimmerPageBuilder.Compare(Side(6073, rows, history), rival, season: 2026);
        var career = SwimmerPageBuilder.Compare(Side(6073, rows, history), rival, season: null);

        Assert.Equal(Dolphin, past.Mine.ClubId);
        Assert.Equal("הפועל דולפין נתניה", past.Mine.ClubName);
        Assert.Equal(Raanana, now.Mine.ClubId);
        // За карьеру — текущий клуб карточки, как в шапке страницы (К6).
        Assert.Equal(Raanana, career.Mine.ClubId);
        Assert.Equal(3, past.Mine.ClubHistory.Count);
        // Без истории сторона остаётся на клубе карточки.
        Assert.Equal("ע.ל.ה עצמה רעננה", past.Rival.ClubName);
        Assert.Empty(past.Rival.ClubHistory);
    }
}
