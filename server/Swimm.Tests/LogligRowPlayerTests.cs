using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Swimm.Infrastructure.Services.DataChecks;
using Swimm.Parsing.Parsers.Loglig;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Игрок loglig у строки (Р122, И-69): разбор id игрока со страницы дисциплины, сопоставление
/// строки страницы с нашей, шаг записи и проверка Health «два игрока loglig на одной карточке».
/// Фикстура — живая страница 100 вольным «גביע ארזי הלבנון» (loglig 9453, дисциплина 41431): в
/// заплыве 2 плывут братья בדארנה — игроки 240949 (дорожка 8) и 240950 (дорожка 1).
/// </summary>
public class LogligRowPlayerTests
{
    private const int Mahmud = 240949, Muhammad = 240950;
    private const int CompId = 454, LogligCompetition = 9453, EventId = 41431;

    private static LogligEventResults Page() => LogligEventResultsParser.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "loglig", "event-41431-100free-two-brothers.html")));

    // ── Разбор ────────────────────────────────────────────────────────────────

    [Fact]
    public void Parser_ReadsPlayerId_OfEveryPersonalRow()
    {
        var ev = Page();

        Assert.False(ev.IsRelay);
        Assert.Equal("100", ev.Distance);
        Assert.NotEmpty(ev.Rows);
        Assert.All(ev.Rows, r => Assert.NotNull(r.PlayerId));

        var mahmud = Assert.Single(ev.Rows, r => r.PlayerId == Mahmud);
        Assert.Equal((2, 8, "01:15.56", 2008), (mahmud.Heat, mahmud.Lane, mahmud.Time, mahmud.BirthYear));
        Assert.Contains("מחמוד", mahmud.FullName);

        var muhammad = Assert.Single(ev.Rows, r => r.PlayerId == Muhammad);
        Assert.Equal((2, 1, "01:14.65", 2008), (muhammad.Heat, muhammad.Lane, muhammad.Time, muhammad.BirthYear));
        Assert.Contains("מוחמד", muhammad.FullName);
    }

    [Fact]
    public void Parser_RowWithoutLink_HasNoPlayerId()
    {
        const string html = """
            <h4>кубок - 06/10/2023</h4><h4>100 חופשי - גברים 12-99 - תוצאות</h4>
            <table><tr><td>1</td><td>דן לוי</td><td>2008</td><td>клуб</td><td>1</td><td>4</td><td>01:00.00</td><td>300</td></tr></table>
            """;

        Assert.Null(Assert.Single(LogligEventResultsParser.Parse(html).Rows).PlayerId);
    }

    // ── Сопоставление ─────────────────────────────────────────────────────────

    private static RowPlayerFromSource Src(int player, int heat, int lane, int? ms, string gender = "male", string date = "06/10/2023") =>
        new(date, "Freestyle", "100", gender, heat, lane, ms, player);

    private static RowPlayerInDb Ours(long id, int heat, int lane, int? ms, string gender = "male", string date = "06/10/2023") =>
        new(id, date, "freestyle", "100", gender, heat, lane, ms);

    [Fact]
    public void Matcher_TimedRows_PairByHeatLaneTime_NamesNotInvolved()
    {
        var plan = LogligRowPlayerMatcher.Build(
            [Src(Mahmud, 2, 8, 75560), Src(Muhammad, 2, 1, 74650)],
            [Ours(10, 2, 8, 75560), Ours(11, 2, 1, 74650)]);

        Assert.Equal(Mahmud, plan.Matches[10]);
        Assert.Equal(Muhammad, plan.Matches[11]);
        Assert.Equal((0, 0), (plan.Ambiguous, plan.SourceUnmatched));
    }

    [Fact]
    public void Matcher_AmbiguousKey_WritesNothing()
    {
        // У нас две строки с тем же заплывом, дорожкой и временем (два возрастных события с
        // заплывами «с 1») — чья страница, не доказать.
        var plan = LogligRowPlayerMatcher.Build(
            [Src(1, 1, 4, 60000)],
            [Ours(10, 1, 4, 60000), Ours(11, 1, 4, 60000)]);

        Assert.Empty(plan.Matches);
        Assert.Equal(1, plan.Ambiguous);

        // И наоборот: две строки источника на одну нашу.
        plan = LogligRowPlayerMatcher.Build([Src(1, 1, 4, 60000), Src(2, 1, 4, 60000)], [Ours(10, 1, 4, 60000)]);
        Assert.Empty(plan.Matches);
        Assert.Equal(2, plan.Ambiguous);
    }

    [Fact]
    public void Matcher_NoTime_PairsByGenderHeatLane()
    {
        // DNS у мальчиков и у девочек на одной дорожке одного заплыва — различает пол.
        var plan = LogligRowPlayerMatcher.Build(
            [Src(1, 1, 4, null, "male"), Src(2, 1, 4, null, "female")],
            [Ours(10, 1, 4, null, "male"), Ours(11, 1, 4, null, "female")]);

        Assert.Equal(1, plan.Matches[10]);
        Assert.Equal(2, plan.Matches[11]);
    }

    [Fact]
    public void Matcher_NoTime_SameGenderTwice_IsAmbiguous()
    {
        var plan = LogligRowPlayerMatcher.Build(
            [Src(1, 1, 4, null), Src(2, 1, 4, null)],
            [Ours(10, 1, 4, null), Ours(11, 1, 4, null)]);

        Assert.Empty(plan.Matches);
        Assert.Equal(2, plan.Ambiguous);
    }

    [Fact]
    public void Matcher_TimeOnOneSideOnly_PairsByLane_ButDifferentTimesDoNot()
    {
        // У нас DQ без времени, страница печатает время с пометкой — та же дорожка, пара доказана.
        var plan = LogligRowPlayerMatcher.Build([Src(1, 1, 4, 60000)], [Ours(10, 1, 4, null)]);
        Assert.Equal(1, plan.Matches[10]);

        // Оба со временем, время разное — расхождение протоколов, не пишем.
        plan = LogligRowPlayerMatcher.Build([Src(1, 1, 4, 60000)], [Ours(10, 1, 4, 61000)]);
        Assert.Empty(plan.Matches);
        Assert.Equal(1, plan.SourceUnmatched);
    }

    [Fact]
    public void Matcher_SourceWithoutDate_SearchesAcrossDays_AndStaysUnique()
    {
        var plan = LogligRowPlayerMatcher.Build(
            [Src(1, 1, 4, 60000, date: "")],
            [Ours(10, 1, 4, 60000, date: "06/10/2023")]);
        Assert.Equal(1, plan.Matches[10]);

        // Та же строка есть в двух днях — без даты не различить.
        plan = LogligRowPlayerMatcher.Build(
            [Src(1, 1, 4, 60000, date: "")],
            [Ours(10, 1, 4, 60000, date: "06/10/2023"), Ours(11, 1, 4, 60000, date: "07/10/2023")]);
        Assert.Empty(plan.Matches);
    }

    [Fact]
    public void Matcher_OtherDay_IsNotMatched()
    {
        var plan = LogligRowPlayerMatcher.Build([Src(1, 1, 4, 60000)], [Ours(10, 1, 4, 60000, date: "07/10/2023")]);

        Assert.Empty(plan.Matches);
        Assert.Equal(1, plan.SourceUnmatched);
    }

    // ── Шаг и проверка Health ─────────────────────────────────────────────────

    private sealed record AuditRow(string Action, string? EntityId, string Summary, string DetailsJson);

    private static LogligEventResultsDto Dto(LogligEventResults ev, bool withLinks = true) => new(
        ev.CompetitionName, ev.Date, ev.StyleName, ev.Distance, ev.Gender, ev.AgeBand, ev.IsRelay,
        ev.Rows.Select(r => new LogligResultRowDto(r.Position, r.Round, r.Category, r.FullName, r.BirthYear, r.Club,
            r.Heat, r.Lane, r.Time, r.FailNote, r.InternationalPoints, r.PersonalPoints, r.ClubPoints, null,
            withLinks ? r.PlayerId : null)).ToList());

    /// <summary>
    /// Соревнование 454 с тремя строками 100 вольным заплыва 2: обе строки братьев лежат на ОДНОЙ
    /// карточке #1 (как после И-69), третья — у соседа по заплыву.
    /// </summary>
    private static SwimmDbContext Db(bool merged = true)
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var ev = Page();
        var other = ev.Rows.First(r => r.Heat == 2 && r.PlayerId is not (Mahmud or Muhammad) && r.Time != null);

        db.DiscoveredCompetitions.Add(new DiscoveredCompetition { OrgCompId = CompId, LogligId = LogligCompetition, Name = "גביע ארזי הלבנון" });
        db.Competitions.Add(new Competition { Id = 1895, OrgCompId = CompId, Name = "גביע ארזי הלבנון", Date = "06/10/2023", PoolType = "25m" });
        db.Styles.Add(new Style { Id = 1, Name = "Freestyle" });
        db.Clubs.Add(new Club { Id = 1, Name = "הפועל לב הגליל סחנין", NameEn = "" });
        db.Swimmers.Add(new Swimmer { Id = 1, LastName = "בדארנה", FirstName = "מחמוד", BirthYear = 2008 });
        db.Swimmers.Add(new Swimmer { Id = 2, LastName = "בדארנה", FirstName = "מוחמד", BirthYear = 2008 });
        db.Swimmers.Add(new Swimmer { Id = 3, LastName = "сосед", FirstName = "по заплыву", BirthYear = 2008 });
        db.Results.Add(Row(10, 1, 2, 8, "1:15.56"));
        db.Results.Add(Row(11, merged ? 1 : 2, 2, 1, "1:14.65"));
        db.Results.Add(Row(12, 3, other.Heat, other.Lane, other.Time!.TrimStart('0')));
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    private static ResultRecord Row(long id, int swimmerId, int heat, int lane, string time) => new()
    {
        Id = id, CompetitionId = 1895, SwimmerId = swimmerId, ClubId = 1, StyleId = 1,
        CompetitionDate = new DateTime(2023, 10, 6), Distance = "100", Gender = "male",
        Heat = heat, Lane = lane, TimeOriginal = time, TimeMillisecond = SwimTime.ParseToMs(time),
    };

    private static (LogligRowPlayerService Service, Mock<ICompetitionDiscoveryProvider> Provider, List<AuditRow> Audit) Build(
        SwimmDbContext db, bool modalHasLinks = true)
    {
        var provider = new Mock<ICompetitionDiscoveryProvider>();
        provider.Setup(p => p.FetchEventIdsAsync(LogligCompetition, It.IsAny<CancellationToken>())).ReturnsAsync([EventId]);
        provider.Setup(p => p.FetchEventResultsAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(Dto(Page(), modalHasLinks));
        provider.Setup(p => p.FetchEventResultsFullPageAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(Dto(Page()));

        var audit = new List<AuditRow>();
        var auditSvc = new Mock<IAdminAuditService>();
        auditSvc.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback((string action, string _, string? id, string? summary, object? details, CancellationToken _) =>
                audit.Add(new AuditRow(action, id, summary!, JsonSerializer.Serialize(details))))
            .Returns(Task.CompletedTask);

        return (new LogligRowPlayerService(db, provider.Object, auditSvc.Object, NullLogger<LogligRowPlayerService>.Instance),
            provider, audit);
    }

    [Fact]
    public async Task Step_WritesPlayerToMatchedRows_AndReportsTwoPlayerCard()
    {
        using var db = Db();
        var (service, provider, audit) = Build(db);

        var report = await service.StampAsync(CompId, "import");

        Assert.True(report.Ok);
        Assert.Equal((1, 2, 3, 3, 1), (report.Events, report.Requests, report.Matched, report.Written, report.TwoPlayerCards));
        var rows = await db.Results.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.LogligPlayerId);
        Assert.Equal(Mahmud, rows[10]);
        Assert.Equal(Muhammad, rows[11]);
        Assert.NotNull(rows[12]);
        // Карточки и принадлежность строк шаг не трогает.
        Assert.Equal(1, (await db.Results.AsNoTracking().SingleAsync(r => r.Id == 11)).SwimmerId);
        Assert.Null((await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 1)).LogligId);
        Assert.Contains("מוחמד", (await db.LogligPlayers.AsNoTracking().SingleAsync(p => p.LogligId == Muhammad)).FullName);
        provider.Verify(p => p.FetchEventResultsFullPageAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        var row = Assert.Single(audit);
        Assert.Equal((LogligRowPlayerRules.AuditAction, "1895"), (row.Action, row.EntityId));
        Assert.Contains("\"Trigger\":\"import\"", row.DetailsJson);
        Assert.Contains("\"TwoPlayerCards\":1", row.DetailsJson);
    }

    [Fact]
    public async Task Step_SecondRun_WritesNothingNew()
    {
        using var db = Db();
        var (service, _, _) = Build(db);
        await service.StampAsync(CompId, "import");

        var again = await service.StampAsync(CompId, "cli");

        Assert.Equal((3, 0), (again.Matched, again.Written));
    }

    [Fact]
    public async Task Step_ModalPageWithoutLinks_FallsBackToFullPage()
    {
        using var db = Db();
        var (service, provider, _) = Build(db, modalHasLinks: false);

        var report = await service.StampAsync(CompId, "import");

        Assert.Equal((3, 3), (report.Matched, report.Requests));
        provider.Verify(p => p.FetchEventResultsFullPageAsync(EventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Step_LogligDown_DoesNotThrow_AndLeavesAuditAndCleanRows()
    {
        using var db = Db();
        var (service, provider, audit) = Build(db);
        provider.Setup(p => p.FetchEventIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("loglig лёг"));

        var report = await service.StampAsync(CompId, "import");

        Assert.False(report.Ok);
        Assert.Contains("не удалось", Assert.Single(audit).Summary);
        Assert.All(await db.Results.AsNoTracking().ToListAsync(), r => Assert.Null(r.LogligPlayerId));
    }

    [Fact]
    public async Task Step_NoLogligId_IsNotOk()
    {
        using var db = Db();
        var (service, provider, _) = Build(db);

        var report = await service.StampAsync(999, "cli");

        Assert.False(report.Ok);
        provider.Verify(p => p.FetchEventIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Check_FindsCardWithTwoPlayers_WithNamesAndRowLayout()
    {
        using var db = Db();
        var (service, _, _) = Build(db);
        await service.StampAsync(CompId, "import");

        var check = new SwimmerTwoLogligPlayersCheck(db);
        var outcome = await check.RunAsync();

        Assert.Equal(DataCheckSeverity.Error, check.Severity);
        Assert.Equal("swimmers.two-loglig-players", check.Id);
        Assert.Equal(1, outcome.Total);
        var item = Assert.Single(outcome.Items);
        Assert.Equal(("Swimmer", 1), (item.EntityType, item.EntityId));
        Assert.Contains("240949", item.Message);
        Assert.Contains("240950", item.Message);
        Assert.Contains("מוחמד", item.Message);
        Assert.Contains("גביע ארזי הלבנון", item.Message);
        // Раскладка: чья строка — по заплыву, дорожке и времени, с id нашей строки.
        Assert.Contains("з.2 д.8 1:15.56 (строка 10)", item.Details);
        Assert.Contains("з.2 д.1 1:14.65 (строка 11)", item.Details);
        Assert.True(item.Details!.IndexOf("240949", StringComparison.Ordinal) < item.Details.IndexOf("строка 10", StringComparison.Ordinal));
        Assert.True(item.Details.IndexOf("240950", StringComparison.Ordinal) < item.Details.IndexOf("строка 11", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Check_EachBrotherOnOwnCard_IsClean()
    {
        using var db = Db(merged: false);
        var (service, _, _) = Build(db);
        var report = await service.StampAsync(CompId, "import");

        Assert.Equal(0, report.TwoPlayerCards);
        Assert.Empty((await new SwimmerTwoLogligPlayersCheck(db).RunAsync()).Items);
    }

    // InMemory не показывает, переводится ли запрос в SQL (группировка с Min/Max по nullable):
    // гоняем проверку на живом Postgres. Только чтение; нет базы или миграции — пропуск.
    [Fact]
    public async Task Check_TranslatesToSql_OnLivePostgres()
    {
        await using var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseNpgsql("Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev").Options);
        try { if (!await db.Database.CanConnectAsync()) return; } catch { return; }

        try
        {
            var outcome = await new SwimmerTwoLogligPlayersCheck(db).RunAsync();
            Assert.True(outcome.Total >= outcome.Items.Count);
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState is "42703" or "42P01")
        {
            // База без миграции ResultLogligPlayer — проверять не на чем.
        }
    }
}
