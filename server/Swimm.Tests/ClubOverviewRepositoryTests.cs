using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Constants;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="ClubOverviewRepository"/> — сборный ответ страницы клуба.
/// Здесь закреплено то, что легко сломать молча: сезон считается по дате соревнования,
/// зачётную группу дают только категории возрастной лестницы, грид не отдаёт все сезоны
/// сразу, а таблица зачёта показывает окно вокруг нашего клуба.
/// </summary>
public class ClubOverviewRepositoryTests
{
    private static SwimmReadDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ClubOverviewRepository Repo(SwimmReadDbContext db) => new(db);

    private static Competition Championship(string date, string pool, string name = "Champ") => new()
    {
        Name = name, Date = date, PoolType = pool, IsChampionship = true
    };

    private static ClubCompetitionStanding Standing(
        Competition comp, Club club, int rank, int points = 100, int gold = 1) => new()
    {
        Competition = comp, Club = club, Rank = rank, Points = points,
        Gold = gold, Silver = 0, Bronze = 0, SwimmerCount = 10, ScoringSwims = 20, SwimCount = 30
    };

    private static async Task<(Club Us, Category Kids)> SeedAsync(SwimmReadDbContext db)
    {
        var us = new Club { Name = "Us", NameEn = "Us" };
        var kids = new Category { Key = "results-kids-team", Name = "Kids", Badge = "K", DisplayOrder = 1 };
        db.AddRange(us, kids);
        await db.SaveChangesAsync();
        return (us, kids);
    }

    private static void Link(SwimmReadDbContext db, Category cat, Competition comp) =>
        db.Add(new CategoryCompetition { Category = cat, Competition = comp });

    // ── Роль зачёта и сезон ─────────────────────────────────────────────────

    [Fact]
    public async Task WinterAndSummer_LandInSameSeason_AsSeparateCells()
    {
        // Зимний чемпионат февраля 2026 и летний июля 2026 — ОДИН сезон 2025/26.
        using var db = CreateDb(nameof(WinterAndSummer_LandInSameSeason_AsSeparateCells));
        var (us, kids) = await SeedAsync(db);
        var winter = Championship("15/02/2026", "25m");
        var summer = Championship("20/07/2026", "50m");
        db.AddRange(winter, summer);
        Link(db, kids, winter);
        Link(db, kids, summer);
        db.AddRange(Standing(winter, us, rank: 3), Standing(summer, us, rank: 1));
        await db.SaveChangesAsync();

        var dto = await Repo(db).GetOverviewAsync(us.Id, us.Id, null, null, 3, null);

        var year = Assert.Single(dto!.Grid);
        Assert.Equal(2025, year.Season);
        Assert.Equal("2025/26", year.Label);
        var row = Assert.Single(year.Rows);
        Assert.Equal(3, row.Winter!.Rank);
        Assert.Equal(1, row.Summer!.Rank);
        Assert.Null(row.OpenWater);
    }

    [Fact]
    public async Task NonChampionship_IsInTimeline_ButNotInGrid()
    {
        using var db = CreateDb(nameof(NonChampionship_IsInTimeline_ButNotInGrid));
        var (us, kids) = await SeedAsync(db);
        var league = new Competition { Name = "League 3", Date = "10/11/2025", PoolType = "25m" };
        db.Add(league);
        Link(db, kids, league);
        db.Add(Standing(league, us, rank: 2));
        await db.SaveChangesAsync();

        var dto = await Repo(db).GetOverviewAsync(us.Id, us.Id, null, null, 3, null);

        Assert.Empty(dto!.Grid);
        var item = Assert.Single(dto.Timeline);
        Assert.Null(item.Kind);          // бейдж ❄/☀ не рисуется
        Assert.Equal(2, item.Rank);
    }

    [Fact]
    public async Task CustomCategory_DoesNotProduceAStandingGroup()
    {
        // result-maccabiah — «соревнование само по себе», клубного зачёта по нему нет.
        using var db = CreateDb(nameof(CustomCategory_DoesNotProduceAStandingGroup));
        var (us, _) = await SeedAsync(db);
        var custom = new Category { Key = "result-maccabiah", Name = "Maccabiah", DisplayOrder = 9 };
        var comp = Championship("15/02/2026", "25m");
        db.AddRange(custom, comp);
        Link(db, custom, comp);
        db.Add(Standing(comp, us, rank: 1));
        await db.SaveChangesAsync();

        var dto = await Repo(db).GetOverviewAsync(us.Id, us.Id, null, null, 3, null);

        Assert.Empty(dto!.Grid);          // в грид не попадает
        Assert.Empty(dto.Groups);         // плитки группы не создаёт
        Assert.Single(dto.Timeline);      // но в истории виден
    }

    [Fact]
    public async Task CompetitionInTwoLadderCategories_GivesTwoGridRows()
    {
        // Реальный случай: Хадера-2026 и Лига-3 висят в Kids + Young одновременно.
        using var db = CreateDb(nameof(CompetitionInTwoLadderCategories_GivesTwoGridRows));
        var (us, kids) = await SeedAsync(db);
        var young = new Category { Key = "results-youth-team", Name = "Young", Badge = "Y", DisplayOrder = 2 };
        var comp = Championship("15/02/2026", "25m");
        db.AddRange(young, comp);
        Link(db, kids, comp);
        Link(db, young, comp);
        db.Add(Standing(comp, us, rank: 4));
        await db.SaveChangesAsync();

        var dto = await Repo(db).GetOverviewAsync(us.Id, us.Id, null, null, 3, null);

        var year = Assert.Single(dto!.Grid);
        Assert.Equal(2, year.Rows.Count);
        Assert.Equal(["results-kids-team", "results-youth-team"], year.Rows.Select(r => r.GroupKey));
    }

    [Fact]
    public async Task CompetitionInTwoCategories_IsCountedOnce_InKpiAndTimeline()
    {
        // Две строки грида — это ДВЕ группы одного и того же старта, а не два старта.
        // Если не схлопнуть, клубу удваиваются очки и медали.
        using var db = CreateDb(nameof(CompetitionInTwoCategories_IsCountedOnce_InKpiAndTimeline));
        var (us, kids) = await SeedAsync(db);
        var young = new Category { Key = "results-youth-team", Name = "Young", Badge = "Y", DisplayOrder = 2 };
        var comp = Championship("15/02/2026", "25m");
        db.AddRange(young, comp);
        Link(db, kids, comp);
        Link(db, young, comp);
        db.Add(Standing(comp, us, rank: 3, points: 500, gold: 2));
        await db.SaveChangesAsync();

        var dto = await Repo(db).GetOverviewAsync(us.Id, us.Id, null, null, 3, null);

        Assert.Equal(500, dto!.Kpi.Points);        // не 1000
        Assert.Equal(2, dto.Kpi.Gold);             // не 4
        Assert.Equal(1, dto.Kpi.Competitions);
        Assert.Single(dto.Timeline);
    }

    // ── Ограничение объёма ──────────────────────────────────────────────────

    [Fact]
    public async Task Grid_ReturnsOnlyRequestedNumberOfSeasons()
    {
        // Сезонов 20+ и число растёт — «все сезоны» никогда не значит «все».
        using var db = CreateDb(nameof(Grid_ReturnsOnlyRequestedNumberOfSeasons));
        var (us, kids) = await SeedAsync(db);
        foreach (var year in new[] { 2023, 2024, 2025, 2026 })
        {
            var comp = Championship($"15/02/{year}", "25m", $"Champ {year}");
            db.Add(comp);
            Link(db, kids, comp);
            db.Add(Standing(comp, us, rank: 1));
        }
        await db.SaveChangesAsync();

        var dto = await Repo(db).GetOverviewAsync(us.Id, us.Id, null, null, gridSeasons: 2, null);

        Assert.Equal(2, dto!.Grid.Count);
        Assert.Equal([2025, 2024], dto.Grid.Select(g => g.Season));   // свежие первыми
        Assert.Equal(4, dto.Seasons.Count);                            // а фильтр знает все
    }

    [Fact]
    public async Task SeasonFilter_NarrowsKpiAndTimeline()
    {
        using var db = CreateDb(nameof(SeasonFilter_NarrowsKpiAndTimeline));
        var (us, kids) = await SeedAsync(db);
        var old = Championship("15/02/2025", "25m", "Old");
        var recent = Championship("15/02/2026", "25m", "Recent");
        db.AddRange(old, recent);
        Link(db, kids, old);
        Link(db, kids, recent);
        db.AddRange(Standing(old, us, rank: 5, points: 50), Standing(recent, us, rank: 2, points: 200));
        await db.SaveChangesAsync();

        var dto = await Repo(db).GetOverviewAsync(us.Id, us.Id, season: 2025, null, 3, null);

        Assert.Equal(200, dto!.Kpi.Points);
        Assert.Equal(2, dto.Kpi.BestRank);
        Assert.Equal(1, dto.Kpi.Competitions);
        Assert.Single(dto.Timeline);
    }

    // ── Таблица зачёта ──────────────────────────────────────────────────────

    [Fact]
    public async Task Standings_ShowsLeadersAndWindowAroundUs()
    {
        // Макет: если мы ниже #4 — показываем 1, 2 и наш ± 1.
        using var db = CreateDb(nameof(Standings_ShowsLeadersAndWindowAroundUs));
        var (us, kids) = await SeedAsync(db);
        var comp = Championship("15/02/2026", "25m");
        db.Add(comp);
        Link(db, kids, comp);
        for (var rank = 1; rank <= 10; rank++)
        {
            var club = rank == 7 ? us : new Club { Name = $"C{rank}", NameEn = $"C{rank}" };
            if (rank != 7) db.Add(club);
            db.Add(Standing(comp, club, rank, points: 1000 - rank));
        }
        await db.SaveChangesAsync();

        var dto = await Repo(db).GetOverviewAsync(us.Id, us.Id, null, null, 3, null);

        var table = dto!.Standings!;
        Assert.Equal(10, table.ClubCount);
        Assert.Equal([1, 2, 6, 7, 8], table.Rows.Select(r => r.Rank));
        Assert.True(table.Rows.Single(r => r.Rank == 7).IsUs);
    }

    [Fact]
    public async Task Standings_TopClub_SeesPlainTopFive()
    {
        using var db = CreateDb(nameof(Standings_TopClub_SeesPlainTopFive));
        var (us, kids) = await SeedAsync(db);
        var comp = Championship("15/02/2026", "25m");
        db.Add(comp);
        Link(db, kids, comp);
        for (var rank = 1; rank <= 8; rank++)
        {
            var club = rank == 1 ? us : new Club { Name = $"C{rank}", NameEn = $"C{rank}" };
            if (rank != 1) db.Add(club);
            db.Add(Standing(comp, club, rank));
        }
        await db.SaveChangesAsync();

        var dto = await Repo(db).GetOverviewAsync(us.Id, us.Id, null, null, 3, null);

        Assert.Equal([1, 2, 3, 4, 5], dto!.Standings!.Rows.Select(r => r.Rank));
    }

    // ── Профиль ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Profile_ReportsRequestedIdAndSeasonRange()
    {
        using var db = CreateDb(nameof(Profile_ReportsRequestedIdAndSeasonRange));
        var (us, kids) = await SeedAsync(db);
        var older = Championship("15/02/2024", "25m", "Older");
        var newer = Championship("15/02/2026", "25m", "Newer");
        db.AddRange(older, newer);
        Link(db, kids, older);
        Link(db, kids, newer);
        db.AddRange(Standing(older, us, rank: 4), Standing(newer, us, rank: 2));
        await db.SaveChangesAsync();

        // requestedId ≠ id — так выглядит запрос по СТАРОМУ id склеенного клуба.
        var dto = await Repo(db).GetOverviewAsync(us.Id, requestedId: 777, null, null, 3, null);

        Assert.Equal(us.Id, dto!.Club.Id);
        Assert.Equal(777, dto.Club.RequestedId);
        Assert.Equal(2023, dto.Club.FirstSeason);
        Assert.Equal(2025, dto.Club.LastSeason);
    }

    [Fact]
    public async Task ClubWithoutStandings_GivesEmptyCardsNotNull()
    {
        // Пустые состояния — норма (клуб без результатов существует, фильтр no-swimmers).
        using var db = CreateDb(nameof(ClubWithoutStandings_GivesEmptyCardsNotNull));
        var (us, _) = await SeedAsync(db);

        var dto = await Repo(db).GetOverviewAsync(us.Id, us.Id, null, null, 3, null);

        Assert.NotNull(dto);
        Assert.Empty(dto.Grid);
        Assert.Empty(dto.Timeline);
        Assert.Empty(dto.Seasons);
        Assert.Null(dto.Standings);
        Assert.Null(dto.Kpi.BestRank);
        Assert.Null(dto.Club.FirstSeason);
    }

    [Fact]
    public async Task UnknownClub_ReturnsNull()
    {
        using var db = CreateDb(nameof(UnknownClub_ReturnsNull));
        Assert.Null(await Repo(db).GetOverviewAsync(999, 999, null, null, 3, null));
    }
}
