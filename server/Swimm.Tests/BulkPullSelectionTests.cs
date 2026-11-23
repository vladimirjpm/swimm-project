using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Swimm.API.Pages.Admin.Competitions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Пачки «за день» / «за месяц» /Admin/Competitions (вариант E, этап 5; хэндофф 4): N на кнопке
/// = число id, которые уйдут в <see cref="IBulkPullService.StartAsync"/>, и сервис берёт их все —
/// для дня, месяца, с чемпионатами и без, с фильтром дисциплины. Отбор один —
/// <see cref="BulkPullSelection"/>; страница (<see cref="IndexModel"/>) считает по нему кнопки.
/// </summary>
public class BulkPullSelectionTests
{
    private static readonly DateOnly Today = new(2026, 11, 23);
    private const string Champ = "אליפות ישראל חורף 2026";

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static DateTime D(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static DiscoveredCompetition Site(int id, DateTime start, string? name = null, DateTime? end = null,
        string? verdict = null, string discipline = "swimming") => new()
    {
        Id = id, OrgCompId = 1000 + id, Name = name ?? $"ליגה {id}",
        DateStart = start, DateEnd = end ?? start, Status = "new",
        AutoPullVerdict = verdict, Discipline = discipline,
    };

    /// <summary>Ноябрь 2026 (сегодня 23.11): все состояния, чемпионаты, чужая дисциплина.</summary>
    private static async Task<SwimmDbContext> SeedAsync(string name)
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);
        db.DiscoveredCompetitions.AddRange(
            Site(2, D(2026, 11, 20)),                                         // 20.11 ждёт
            Site(3, D(2026, 11, 20)),                                         // 20.11 ждёт
            Site(4, D(2026, 11, 20), Champ),                                  // 20.11 ждёт, чемпионат
            Site(5, D(2026, 11, 21), Champ + " ארנה"),                        // 21.11 — только чемпионат
            Site(6, D(2026, 11, 21), verdict: ResultsAutoVerdicts.Empty),     // ждём протокол
            Site(7, D(2026, 11, 22), end: D(2026, 11, 24)),                   // идёт
            Site(8, D(2026, 11, 28)),                                         // впереди
            Site(9, D(2026, 11, 19), discipline: "artistic"),                 // 19.11 ждёт, артистическое
            Site(10, D(2026, 11, 19)),                                        // 19.11 ждёт
            Site(11, D(2026, 11, 14), verdict: ResultsAutoVerdicts.NotFinished)); // ждём протокол
        await db.SaveChangesAsync();
        return db;
    }

    private static Task<UnifiedCompetitionList> List(SwimmDbContext db, string? discipline = null) =>
        new CompetitionAdminRepository(db).GetUnifiedAsync(null, null, 2027, null, showSynthetic: false,
            11, 1, 20, discipline: discipline, today: Today, unpaged: true);

    /// <summary>Сервис пачки с теми же строками; Total = сколько реально ушло в работу.</summary>
    private static async Task<int> SentToService(IEnumerable<UnifiedCompetitionRowDto> rows, BulkPullPick pick, bool champ)
    {
        var dtos = rows.Where(u => u.Site != null)
            .Select(u => BulkPullServiceTests.Row(u.Site!.DiscoveredId, u.Site.Name)).ToArray();
        var (service, _, _) = BulkPullServiceTests.Build(dtos);
        var batch = await service.StartAsync(pick.Ids, champ);
        Assert.Equal(pick.Ids.OrderBy(x => x), batch.QueueIds!.OrderBy(x => x));
        return batch.Total;
    }

    [Fact]
    public async Task Month_WithoutAndWithChampionships_ButtonEqualsWhatTheServiceTakes()
    {
        await using var db = await SeedAsync(nameof(Month_WithoutAndWithChampionships_ButtonEqualsWhatTheServiceTakes));
        var items = (await List(db)).Page.Items;
        var offer = BulkPullSelection.Offer(items);

        // Без чемпионатов: 2, 3, 10 (артистическое скрыто фильтром дисциплины по умолчанию).
        Assert.Equal([10, 2, 3], offer.WithoutChampionships.Ids);
        Assert.Equal(new[] { 10, 2, 4, 3, 5 }.OrderBy(x => x), offer.WithChampionships.Ids.OrderBy(x => x));
        Assert.Equal(offer.WithoutChampionships.Count, await SentToService(items, offer.WithoutChampionships, false));
        Assert.Equal(offer.WithChampionships.Count, await SentToService(items, offer.WithChampionships, true));
    }

    [Fact]
    public async Task NeverTakes_WaitingProtocol_Running_Ahead()
    {
        await using var db = await SeedAsync(nameof(NeverTakes_WaitingProtocol_Running_Ahead));
        var ids = BulkPullSelection.Offer((await List(db)).Page.Items).WithChampionships.Ids;
        Assert.DoesNotContain(6, ids);
        Assert.DoesNotContain(11, ids);
        Assert.DoesNotContain(7, ids);
        Assert.DoesNotContain(8, ids);
    }

    [Fact]
    public async Task Day_TakesExactlyThatDay_AndAChampionshipOnlyDayHasNoButton()
    {
        await using var db = await SeedAsync(nameof(Day_TakesExactlyThatDay_AndAChampionshipOnlyDayHasNoButton));
        var items = (await List(db)).Page.Items;
        var days = CompetitionRowStateRules.GroupReadyToPullByDay(items, u => u.Status)
            .ToDictionary(d => d.Day, d => BulkPullSelection.Offer(d.Rows));

        var d20 = days[new DateOnly(2026, 11, 20)];
        Assert.Equal([2, 3], d20.WithoutChampionships.Ids);
        Assert.Equal(3, d20.WithChampionships.Count);
        Assert.Equal(2, await SentToService(items, d20.WithoutChampionships, false));
        Assert.Equal(3, await SentToService(items, d20.WithChampionships, true));

        var d21 = days[new DateOnly(2026, 11, 21)];
        Assert.True(d21.OnlyChampionships);           // «чемпионат — отдельно»
        Assert.Equal(0, d21.WithoutChampionships.Count);
        Assert.Equal([5], d21.WithChampionships.Ids);
        Assert.Equal(1, await SentToService(items, d21.WithChampionships, true));
    }

    [Fact]
    public async Task DisciplineFilter_ChangesTheButton_AndTheBatch()
    {
        await using var db = await SeedAsync(nameof(DisciplineFilter_ChangesTheButton_AndTheBatch));
        var all = (await List(db, discipline: "all")).Page.Items;
        var offer = BulkPullSelection.Offer(all);
        Assert.Contains(9, offer.WithoutChampionships.Ids);
        Assert.Equal(4, offer.WithoutChampionships.Count);
        Assert.Equal(4, await SentToService(all, offer.WithoutChampionships, false));

        var artistic = (await List(db, discipline: "artistic")).Page.Items;
        Assert.Equal([9], BulkPullSelection.Offer(artistic).WithoutChampionships.Ids);
    }

    [Fact]
    public void Cap_TakesTheEarliest_AndSaysHowManyAreLeft()
    {
        var rows = Enumerable.Range(1, 35).Select(i => new UnifiedCompetitionRowDto
        {
            Site = new UnifiedSiteInfo { DiscoveredId = i, Name = $"ליגה {i}", DateStart = D(2026, 10, 1).AddDays(35 - i) },
            Status = new CompetitionRowStatus(CompetitionRowState.ReadyToPull),
        }).ToList();

        var pick = BulkPullSelection.Pick(rows, includeChampionships: false);
        Assert.Equal(BulkPullLimits.MaxBatchSize, pick.Count);
        Assert.Equal(35, pick.Eligible);
        Assert.Equal(5, pick.Left);
        Assert.Equal(35, pick.Ids[0]); // самая ранняя дата
    }

    [Fact]
    public void AlreadyInTheDatabase_IsNotACandidate()
    {
        // Недотянутая многодневка (Р115): «ждёт затягивания», но дни уже в базе — в пачку не идёт.
        var r115 = new UnifiedCompetitionRowDto
        {
            Db = new CompetitionRowDto(),
            Site = new UnifiedSiteInfo { DiscoveredId = 1, Name = "x", DateStart = D(2026, 11, 1) },
            Status = new CompetitionRowStatus(CompetitionRowState.ReadyToPull),
        };
        Assert.False(BulkPullSelection.IsCandidate(r115));
        Assert.True(BulkPullSelection.Offer([r115]).IsEmpty);
    }

    // ── страница: где кнопки есть ────────────────────────────────────────────

    private static ISeriesAdminRepository Series()
    {
        var m = new Mock<ISeriesAdminRepository>();
        m.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<SeriesRowDto>());
        return m.Object;
    }

    private static async Task<IndexModel> GetPage(SwimmDbContext db, string query)
    {
        var page = new IndexModel(new CompetitionAdminRepository(db), Mock.Of<IImportService>(),
            Mock.Of<IPointRulesAdminRepository>(), Mock.Of<IAdminAuditService>(), new ConfigurationBuilder().Build(),
            Mock.Of<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(), NullLogger<IndexModel>.Instance,
            Series(), new FixedTime(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero)));
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString(query);
        page.PageContext = new PageContext { HttpContext = http };
        foreach (var (k, v) in Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query))
        {
            switch (k)
            {
                case "season": page.SeasonParam = v; break;
                case "month": page.Month = int.Parse(v!); break;
                case "q": page.Search = v; break;
            }
        }
        await page.OnGetAsync();
        return page;
    }

    [Fact]
    public async Task Page_MonthButton_OnlyWithAMonthOfASeason()
    {
        await using var db = await SeedAsync(nameof(Page_MonthButton_OnlyWithAMonthOfASeason));

        var month = await GetPage(db, "?season=2027&month=11");
        Assert.NotNull(month.MonthBulk);
        Assert.Equal([10, 2, 3], month.MonthBulk!.WithoutChampionships.Ids);
        // Сумма кнопок дней = кнопка месяца (оба положения чекбокса).
        Assert.Equal(month.MonthBulk.WithoutChampionships.Count, month.DayBulk.Values.Sum(o => o.WithoutChampionships.Count));
        Assert.Equal(month.MonthBulk.WithChampionships.Count, month.DayBulk.Values.Sum(o => o.WithChampionships.Count));

        var season = await GetPage(db, "?season=2027");
        Assert.Null(season.MonthBulk); // кнопки «за сезон» нет
    }
}
