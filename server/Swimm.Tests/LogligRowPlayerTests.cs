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

    private static LogligEventResults Page(string file = "event-41431-100free-two-brothers.html") =>
        LogligEventResultsParser.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "loglig", file)));

    // Запасной источник — ПОЛНАЯ (не печатная) страница той же дисциплины, как её отдаёт сайт:
    // с шапкой, меню и скриптами. Разбор обязан дать те же строки и тех же игроков.
    [Fact]
    public void Parser_FullPage_GivesSameRowsAndPlayers_AsModal()
    {
        var modal = Page();
        var full = Page("event-41431-100free-two-brothers-fullpage.html");

        Assert.Equal(modal.Rows.Count, full.Rows.Count);
        Assert.Equal((modal.StyleName, modal.Distance, modal.Gender, modal.Date), (full.StyleName, full.Distance, full.Gender, full.Date));
        Assert.Equal(
            modal.Rows.Select(r => (r.PlayerId, r.Heat, r.Lane, r.Time)),
            full.Rows.Select(r => (r.PlayerId, r.Heat, r.Lane, r.Time)));
        Assert.Contains(full.Rows, r => r.PlayerId == Mahmud && r.Lane == 8);
        Assert.Contains(full.Rows, r => r.PlayerId == Muhammad && r.Lane == 1);
    }

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

    // Прочитаны не все страницы — без времени не сводим: двойник мог остаться на непрочитанной.
    [Fact]
    public void Matcher_UntimedOff_LeavesNoTimeRowsAlone_ButPairsTimed()
    {
        var plan = LogligRowPlayerMatcher.Build(
            [Src(1, 1, 4, null), Src(2, 2, 5, 60000), Src(3, 3, 3, 61000)],
            [Ours(10, 1, 4, null), Ours(11, 2, 5, 60000), Ours(12, 3, 3, null)],
            allowUntimed: false);

        Assert.Equal(2, Assert.Single(plan.Matches).Value);
        Assert.Equal(2, plan.SourceUnmatched);
    }

    // Одну нашу строку берут строка источника с датой и строка без даты: не доказана ни одна,
    // и в счёт неоднозначных они входят ровно по разу.
    [Fact]
    public void Matcher_ContestedRow_CountsEachClaimantOnce()
    {
        var plan = LogligRowPlayerMatcher.Build(
            [Src(1, 1, 4, 60000), Src(2, 1, 4, 60000, date: "")],
            [Ours(10, 1, 4, 60000)]);

        Assert.Empty(plan.Matches);
        Assert.Equal((2, 0), (plan.Ambiguous, plan.SourceUnmatched));
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
        db.Results.Add(Row(12, 3, other.Heat, other.Lane, other.Time!));
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
        Assert.Equal((DataCheckFixKinds.SwimmerInCompetition, 1895), (item.FixKind, item.FixEntityId));
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

    // ── Замечания ревью: непрочитанные страницы, время, запасная страница ─────

    private const int OtherEvent = 41432;

    private static LogligEventResultsDto RelayPage() =>
        new("кубок", "06/10/2023", "Freestyle", "4X50", "male", "", true, []);

    /// <summary>Страница 41431 плюс строка-неявка (заплыв 9, дорожка 9, игрок 777).</summary>
    private static LogligEventResultsDto PageWithNoShow()
    {
        var dto = Dto(Page());
        return dto with
        {
            Rows = [.. dto.Rows, new LogligResultRowDto(null, "", "", "неявка", 2008, "клуб", 9, 9, null, "NS", 0, null, null, null, 777)],
        };
    }

    private static async Task AddNoShowRowAsync(SwimmDbContext db)
    {
        var row = Row(13, 3, 9, 9, "");
        row.TimeMillisecond = null;
        row.TimeFail = true;
        row.TimeFailNote = "NS";
        db.Results.Add(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Step_AllPagesRead_NoShowRowGetsItsPlayer()
    {
        using var db = Db();
        await AddNoShowRowAsync(db);
        var (service, provider, _) = Build(db);
        provider.Setup(p => p.FetchEventResultsAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(PageWithNoShow());

        var report = await service.StampAsync(CompId, "import");

        Assert.False(report.Incomplete);
        Assert.Equal(777, (await db.Results.AsNoTracking().SingleAsync(r => r.Id == 13)).LogligPlayerId);
    }

    // Таймаут HttpClient — TaskCanceledException при НЕотменённом токене: это «дисциплина не
    // прочитана», а не срыв прогона. И раз прочитано не всё — строки без времени не сводим.
    [Fact]
    public async Task Step_PageTimeout_IsUnreadableDiscipline_TimedRowsWritten_NoShowRowsNot()
    {
        using var db = Db();
        await AddNoShowRowAsync(db);
        var (service, provider, audit) = Build(db);
        provider.Setup(p => p.FetchEventIdsAsync(LogligCompetition, It.IsAny<CancellationToken>())).ReturnsAsync([EventId, OtherEvent]);
        provider.Setup(p => p.FetchEventResultsAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(PageWithNoShow());
        provider.Setup(p => p.FetchEventResultsAsync(OtherEvent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var report = await service.StampAsync(CompId, "import");

        Assert.True(report.Ok);
        Assert.True(report.Incomplete);
        Assert.Equal([OtherEvent], report.Unreadable);
        Assert.Equal(3, report.Written);
        var rows = await db.Results.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.LogligPlayerId);
        Assert.Equal(Mahmud, rows[10]);
        Assert.Null(rows[13]);
        Assert.Contains("строки без времени не сопоставлялись", Assert.Single(audit).Summary);
    }

    // Отмена НАШЕГО токена (потолок, остановка процесса) — не «дисциплина не прочитана».
    [Fact]
    public async Task Step_OurCancellation_Propagates_WithAudit()
    {
        using var db = Db();
        var (service, provider, audit) = Build(db);
        using var cts = new CancellationTokenSource();
        provider.Setup(p => p.FetchEventResultsAsync(EventId, It.IsAny<CancellationToken>()))
            .Returns(async () => { await cts.CancelAsync(); throw new OperationCanceledException(cts.Token); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StampAsync(CompId, "import", cts.Token));

        Assert.Contains("не удалось", Assert.Single(audit).Summary);
    }

    // Истёк бюджет чтения: прочитанное сопоставлено и записано, остаток посчитан — работа не потеряна.
    [Fact]
    public async Task Step_ReadBudgetExpired_SavesWhatWasRead_AndReportsTheRest()
    {
        using var db = Db();
        var (service, provider, _) = Build(db);
        provider.Setup(p => p.FetchEventIdsAsync(LogligCompetition, It.IsAny<CancellationToken>()))
            .ReturnsAsync([EventId, OtherEvent, OtherEvent + 1]);
        provider.Setup(p => p.FetchEventResultsAsync(EventId, It.IsAny<CancellationToken>()))
            .Returns(async () => { await Task.Delay(80); return Dto(Page()); });

        var report = await service.StampAsync(CompId, "import", default, TimeSpan.FromMilliseconds(20));

        Assert.True(report.Ok);
        Assert.Equal((2, 3, true), (report.NotReached, report.Written, report.Incomplete));
        Assert.Equal(Muhammad, (await db.Results.AsNoTracking().SingleAsync(r => r.Id == 11)).LogligPlayerId);
        provider.Verify(p => p.FetchEventResultsAsync(OtherEvent, It.IsAny<CancellationToken>()), Times.Never);
    }

    // Запасная полная страница тоже без ссылок — вёрстка сменилась у обеих: второй раз не пробуем.
    [Fact]
    public async Task Step_FullPageAlsoWithoutLinks_IsTriedOnlyOnce()
    {
        using var db = Db();
        var (service, provider, _) = Build(db, modalHasLinks: false);
        provider.Setup(p => p.FetchEventIdsAsync(LogligCompetition, It.IsAny<CancellationToken>())).ReturnsAsync([EventId, OtherEvent]);
        provider.Setup(p => p.FetchEventResultsAsync(OtherEvent, It.IsAny<CancellationToken>())).ReturnsAsync(Dto(Page(), withLinks: false));
        provider.Setup(p => p.FetchEventResultsFullPageAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Dto(Page(), withLinks: false));

        var report = await service.StampAsync(CompId, "import");

        provider.Verify(p => p.FetchEventResultsFullPageAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal((0, 4), (report.Matched, report.Requests));   // список + 2 страницы + 1 запасная
        Assert.True(report.NoLink > 0);
    }

    // ── Проверка: многодневка и ключ находки ──────────────────────────────────

    /// <summary>Событие из двух дней; у карточки #1 в день 1 строка одного игрока, в день 2 — другого.</summary>
    private static SwimmDbContext MultiDayDb()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.CompetitionEvents.Add(new CompetitionEvent { Id = 7, Name = "чемпионат" });
        db.Competitions.Add(new Competition { Id = 21, EventId = 7, OrgCompId = 500, Name = "чемпионат", Date = "01/02/2026", PoolType = "50m" });
        db.Competitions.Add(new Competition { Id = 22, EventId = 7, Name = "чемпионат", Date = "02/02/2026", PoolType = "50m" });
        db.Competitions.Add(new Competition { Id = 30, Name = "кубок", Date = "09/03/2026", PoolType = "25m" });
        db.Styles.Add(new Style { Id = 1, Name = "Freestyle" });
        db.Clubs.Add(new Club { Id = 1, Name = "клуб", NameEn = "" });
        db.Swimmers.Add(new Swimmer { Id = 1, LastName = "בדארנה", FirstName = "מחמוד", BirthYear = 2008 });
        db.LogligPlayers.Add(new LogligPlayer { LogligId = Mahmud, FullName = "מחמוד היתם בדארנה", SeenAt = DateTime.UtcNow });
        db.LogligPlayers.Add(new LogligPlayer { LogligId = Muhammad, FullName = "מוחמד היתם בדארנה", SeenAt = DateTime.UtcNow });
        db.SaveChanges();
        return db;
    }

    private static ResultRecord PlayerRow(long id, int competitionId, int player, int lane) => new()
    {
        Id = id, CompetitionId = competitionId, SwimmerId = 1, ClubId = 1, StyleId = 1,
        CompetitionDate = new DateTime(2026, 2, 1), Distance = "100", Gender = "male",
        Heat = 2, Lane = lane, TimeOriginal = "1:15.56", TimeMillisecond = 75560, LogligPlayerId = player,
    };

    // Братья плыли в РАЗНЫЕ дни одного события: по дню у карточки один игрок, по событию — два.
    [Fact]
    public async Task Check_BrothersOnDifferentDaysOfOneEvent_IsOneFindingForTheEvent()
    {
        using var db = MultiDayDb();
        db.Results.AddRange(PlayerRow(1, 21, Mahmud, 8), PlayerRow(2, 22, Muhammad, 1));
        await db.SaveChangesAsync();

        var outcome = await new SwimmerTwoLogligPlayersCheck(db).RunAsync();

        Assert.Equal(1, outcome.Total);
        var item = Assert.Single(outcome.Items);
        Assert.Equal((1, 21), (item.EntityId, item.FixEntityId));   // пловец + первый день события
        Assert.Contains("дни #21, #22", item.Message);
        Assert.Contains("день #21 100 Freestyle з.2 д.8", item.Details);
        Assert.Contains("день #22 100 Freestyle з.2 д.1", item.Details);
    }

    // Один игрок в одном старте и другой — в ДРУГОМ соревновании: это не находка (у loglig бывают
    // дубли игрока, а сезон от сезона запись могли завести заново).
    [Fact]
    public async Task Check_DifferentPlayersInDifferentCompetitions_IsClean()
    {
        using var db = MultiDayDb();
        db.Results.AddRange(PlayerRow(1, 21, Mahmud, 8), PlayerRow(2, 30, Muhammad, 1));
        await db.SaveChangesAsync();

        Assert.Empty((await new SwimmerTwoLogligPlayersCheck(db).RunAsync()).Items);
    }

    // Ключ находки — пловец + соревнование: «принято» в одном старте не глушит ту же карточку в другом.
    [Fact]
    public async Task Finding_AcceptedInOneCompetition_StillOpensInAnother()
    {
        using var db = MultiDayDb();
        db.Results.AddRange(PlayerRow(1, 21, Mahmud, 8), PlayerRow(2, 21, Muhammad, 1));
        await db.SaveChangesAsync();
        var runner = new DataCheckRunner(db, [new SwimmerTwoLogligPlayersCheck(db)]);
        await runner.RunAllAsync("test");
        var first = await db.DataCheckFindings.SingleAsync();
        Assert.True(await runner.AcceptAsync(first.Id, "один человек, две записи на loglig"));

        // Позже та же карточка получает строки двух игроков в другом соревновании.
        db.Results.AddRange(PlayerRow(3, 30, Mahmud, 8), PlayerRow(4, 30, Muhammad, 1));
        await db.SaveChangesAsync();
        await runner.RunAllAsync("test");

        var findings = await db.DataCheckFindings.AsNoTracking().OrderBy(f => f.FixEntityId).ToListAsync();
        Assert.Equal([21, 30], findings.Select(f => f.FixEntityId));
        Assert.Equal(DataCheckResolutions.Accepted, findings[0].Resolution);
        Assert.Null(findings[1].Resolution);
    }

    // InMemory не показывает, переводится ли запрос в SQL (группировка с COALESCE и Min/Max по
    // nullable): гоняем проверку на живом Postgres. Только чтение. Пропуск — если базы нет или в
    // ней ещё нет миграции; на мигрированной базе любая ошибка SQL — падение теста.
    [Fact]
    public async Task Check_TranslatesToSql_OnLivePostgres()
    {
        await using var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseNpgsql("Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev").Options);
        try { if (!await db.Database.CanConnectAsync()) return; } catch { return; }
        if (!(await db.Database.GetAppliedMigrationsAsync()).Any(m => m.EndsWith("_ResultLogligPlayer", StringComparison.Ordinal)))
            return;

        var outcome = await new SwimmerTwoLogligPlayersCheck(db).RunAsync();

        Assert.True(outcome.Total >= outcome.Items.Count);
    }
}
