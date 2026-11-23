using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
/// Счётчики состояний на чипах сезонов и месяцев /Admin/Competitions (этап 1 «варианта E»):
/// закрыто + долг + впереди = всего; сумма месяцев = сезон; фильтр <c>?state=</c> счётчики не
/// трогает; выбранный месяц — без пагинации. И редирект быстрой правки — с теми же параметрами.
/// </summary>
public class CompetitionStateCountersTests
{
    private static readonly DateOnly Today = new(2026, 11, 23);

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static DiscoveredCompetition Site(int id, DateTime start, DateTime? end = null,
        string status = "new", string? verdict = null, DateTime? empty = null) => new()
    {
        Id = id, OrgCompId = 1000 + id, Name = $"Старт {id}",
        DateStart = start, DateEnd = end ?? start, Status = status,
        AutoPullVerdict = verdict, EmptySourceAt = empty,
    };

    private static DateTime D(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<SwimmDbContext> SeedAsync(string name)
    {
        var db = CreateDb(name);
        // Затянутое (закрыто) — октябрь 2026.
        db.Competitions.Add(new Competition { Id = 1, Name = "Старт 1", Date = "10/10/2026", PoolType = "25m", OrgCompId = 1001 });
        db.DiscoveredCompetitions.AddRange(
            Site(1, D(2026, 10, 10), status: "imported"),
            Site(2, D(2026, 11, 20)),                                          // ждёт затягивания
            Site(3, D(2026, 11, 21), verdict: ResultsAutoVerdicts.Empty),       // ждём протокол
            Site(4, D(2026, 11, 22), D(2026, 11, 24)),                         // идёт (день 2 из 3)
            Site(5, D(2026, 11, 28)),                                          // впереди
            Site(6, D(2026, 11, 14), empty: D(2026, 11, 15)),                  // ∅ — закрыто
            Site(7, D(2026, 12, 5)),                                           // впереди, декабрь
            Site(8, D(2026, 10, 3), status: "ignored"),                        // скрыто — закрыто
            Site(9, D(2024, 1, 10)));                                          // 23-24: долг
        await db.SaveChangesAsync();
        return db;
    }

    private static Task<UnifiedCompetitionList> Get(SwimmDbContext db, int? season = null, int? month = null,
        string? state = null, int page = 1, bool unpaged = false) =>
        new CompetitionAdminRepository(db).GetUnifiedAsync(null, null, season, null, showSynthetic: false,
            month, page, 20, state: state, today: Today, unpaged: unpaged);

    [Fact]
    public async Task EveryRowGetsAState_AndTheTableMatchesTheRule()
    {
        await using var db = await SeedAsync(nameof(EveryRowGetsAState_AndTheTableMatchesTheRule));
        var list = await Get(db);

        var byName = list.Page.Items.ToDictionary(u => u.Site?.Name ?? u.Db!.Single!.Name, u => u.Status!);
        Assert.Equal(CompetitionRowState.Closed, byName["Старт 1"].State);
        Assert.Equal(CompetitionRowState.ReadyToPull, byName["Старт 2"].State);
        Assert.Equal(CompetitionRowState.WaitingProtocol, byName["Старт 3"].State);
        Assert.Equal(WaitingProtocolReason.NoProtocol, byName["Старт 3"].Reason);
        Assert.Equal(CompetitionRowState.Running, byName["Старт 4"].State);
        Assert.Equal((2, 3), (byName["Старт 4"].RunningDay!.Value, byName["Старт 4"].DayCount));
        Assert.Equal(CompetitionRowState.Ahead, byName["Старт 5"].State);
        Assert.True(byName["Старт 6"].NothingToPull);
        Assert.Equal(CompetitionRowState.Closed, byName["Старт 8"].State);
        Assert.Equal(CompetitionRowState.ReadyToPull, byName["Старт 9"].State); // 23-24 — долг
    }

    [Fact]
    public async Task Invariants_BucketsSumToTotal_MonthsSumToSeason()
    {
        await using var db = await SeedAsync(nameof(Invariants_BucketsSumToTotal_MonthsSumToSeason));

        var all = await Get(db);
        foreach (var s in all.SeasonCounts!)
        {
            Assert.Equal(s.Total, s.States.Total);
            Assert.Equal(s.Total, s.States.Closed + s.States.Debt + s.States.AheadTotal);
        }

        var s2027 = all.SeasonCounts!.Single(s => s.Season == 2027).States;
        Assert.Equal(3, s2027.Closed);      // затянутое, ∅, скрытое
        Assert.Equal(2, s2027.Debt);        // ждёт затягивания + ждём протокол
        Assert.Equal(3, s2027.AheadTotal);  // идёт + 2 впереди
        Assert.Equal(1, all.SeasonCounts!.Single(s => s.Season == 2024).States.Debt);

        // Месяцы — с фильтром сезона, без месяца; их сумма = сезон.
        var season = await Get(db, season: 2027);
        var monthSum = season.MonthStates!.Aggregate(CompetitionStateCounts.Zero, (a, m) => a + m);
        Assert.Equal(s2027, monthSum);
        for (var i = 0; i < 12; i++)
            Assert.Equal(season.MonthCounts[i], season.MonthStates![i].Total);
        Assert.Equal(2, season.MonthStates![10].Debt); // ноябрь
        Assert.Equal(1, season.MonthStates![11].AheadTotal); // декабрь
    }

    [Fact]
    public async Task StateFilter_FiltersRows_ButNotTheCounters()
    {
        await using var db = await SeedAsync(nameof(StateFilter_FiltersRows_ButNotTheCounters));

        var plain = await Get(db, season: 2027, month: 11, unpaged: true);
        var waiting = await Get(db, season: 2027, month: 11, state: CompetitionStateFilters.Waiting, unpaged: true);

        Assert.Equal(new[] { "Старт 3", "Старт 2" }, waiting.Page.Items.Select(u => u.Site!.Name));
        Assert.Equal(plain.SeasonCounts, waiting.SeasonCounts);
        Assert.Equal(plain.MonthStates, waiting.MonthStates);
        // Числа переключателя — под месяцем, но без state: все 5 ноябрьских строк.
        Assert.Equal(5, waiting.ListStates!.Total);
        Assert.Equal(plain.ListStates, waiting.ListStates);
        Assert.Equal(2, waiting.ListStates!.Debt);
    }

    [Fact]
    public async Task Unpaged_ReturnsTheWholeMonth_PagedKeepsTwenty()
    {
        await using var db = CreateDb(nameof(Unpaged_ReturnsTheWholeMonth_PagedKeepsTwenty));
        for (var i = 1; i <= 25; i++)
            db.DiscoveredCompetitions.Add(Site(i, D(2026, 11, 1 + i % 28)));
        await db.SaveChangesAsync();

        var month = await Get(db, season: 2027, month: 11, page: 3, unpaged: true);
        Assert.Equal(25, month.Page.Items.Count);
        Assert.Equal(1, month.Page.Page);
        Assert.Equal(1, month.Page.TotalPages);

        var paged = await Get(db, season: 2027);
        Assert.Equal(20, paged.Page.Items.Count);
        Assert.Equal(2, paged.Page.TotalPages);
    }

    // ── Редирект после быстрой правки (баг из брифа: терялись discipline и filter) ──

    [Fact]
    public async Task QuickSave_RedirectKeepsEveryListParameter()
    {
        var repo = new Mock<ICompetitionAdminRepository>();
        repo.Setup(r => r.QuickUpdateAsync(It.IsAny<CompetitionQuickEditDto>()))
            .ReturnsAsync(CompetitionSaveResult.Ok(1));
        var page = new IndexModel(repo.Object, Mock.Of<IImportService>(), Mock.Of<IPointRulesAdminRepository>(),
            Mock.Of<IAdminAuditService>(), new ConfigurationBuilder().Build(),
            Mock.Of<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(), NullLogger<IndexModel>.Instance,
            Mock.Of<ISeriesAdminRepository>(), TimeProvider.System)
        {
            Search = "ליגה", CategoryKey = "results-main", SeasonParam = "2027", Kind = "champ",
            Discipline = "all", Stage = "OnSite", FilterAlias = "no-results", ShowSynthetic = true,
            Month = null, PageNumber = 2, State = "waiting", Selected = "c12",
        };
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        page.PageContext = new PageContext { HttpContext = http };
        page.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());

        var result = Assert.IsType<RedirectToPageResult>(await page.OnPostQuickSaveAsync(
            1, "25m", false, false, false, null, null, null));

        var rv = result.RouteValues!;
        Assert.Equal("ליגה", rv["q"]);
        Assert.Equal("results-main", rv["cat"]);
        Assert.Equal("2027", rv["season"]);
        Assert.Equal("champ", rv["kind"]);
        Assert.Equal("all", rv["discipline"]);
        Assert.Equal("OnSite", rv["stage"]);
        Assert.Equal("no-results", rv["filter"]);
        Assert.Equal("true", rv["synth"]);
        Assert.Equal("2", rv["p"]);
        Assert.Equal("waiting", rv["state"]);
        Assert.Equal("c12", rv["sel"]);
        Assert.False(rv.ContainsKey("month"));
    }
}
