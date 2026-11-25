using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Swimm.API.Controllers;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Swimm.Infrastructure.Services.DataChecks;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Р125/Р126: пол пловца с карточки loglig — сервис (выборка, суточный ответ, потолок запросов,
/// подтверждение личности заплывом, пропуск случаев с серьёзными последствиями, условная запись,
/// замок, обрыв по времени, сбой на одном пловце), находки и кнопки Health.
/// Сеть — подставной <see cref="ILogligClient"/>.
/// </summary>
public class LogligCardGenderServiceTests
{
    private static readonly DateTime SwimDate = new(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc);

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class FakeLoglig(Dictionary<int, LogligPlayerCard?> cards) : ILogligClient
    {
        public List<int> Calls { get; } = [];
        public Action<int>? OnCall { get; set; }

        public Task<LogligPlayerCard?> GetPlayerCardAsync(int logligId, int? seasonId = null, CancellationToken ct = default)
        {
            Calls.Add(logligId);
            OnCall?.Invoke(logligId);
            return Task.FromResult(cards.GetValueOrDefault(logligId));
        }

        public string BuildPublicProfileUrl(int logligId, int? seasonId = null, bool resultsTab = false) => "";
        public Task<int?> GetCompetitionSeasonIdAsync(int competitionLogligId, CancellationToken ct = default) => Task.FromResult<int?>(null);
        public Task<LogligCompetitionStanding?> GetCompetitionStandingAsync(int logligId, int scaleSampleEvents = 12, CancellationToken ct = default) =>
            Task.FromResult<LogligCompetitionStanding?>(null);
        public Task<LogligRegulationDoc?> GetRegulationAsync(int logligId, CancellationToken ct = default) =>
            Task.FromResult<LogligRegulationDoc?>(null);
        public Task<IReadOnlyList<LogligParticipant>> GetCompetitionParticipantsAsync(int competitionLogligId,
            IReadOnlyCollection<string>? wanted = null, int maxEvents = 60, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LogligParticipant>>([]);
    }

    /// <summary>Сухой расчёт последствий — подставной: по умолчанию «последствий нет».</summary>
    private sealed class FakeImpact : IGenderImpactService
    {
        public Dictionary<int, GenderImpact> BySwimmer { get; } = [];
        public Func<int, Task>? OnAssess { get; set; }

        public async Task<GenderImpact> AssessAsync(int swimmerId, string gender, CancellationToken ct = default)
        {
            if (OnAssess is not null) await OnAssess(swimmerId);
            return BySwimmer.GetValueOrDefault(swimmerId, GenderImpact.None);
        }
    }

    private sealed class World
    {
        public required string DbName;
        public required SwimmDbContext Db;
        public required FakeLoglig Loglig;
        public readonly Mock<IAdminAuditService> Audit = new();
        public readonly Mock<ICompetitionRecalculationService> Recalc = new();
        public readonly FakeImpact Impact = new();
        public readonly LogligCardGenderGate Gate = new();
        public IDataCheckRunner? Runner;
        public int CompId;

        public LogligGenderReviewFinder Finder() => new(Db, Impact);

        public LogligCardGenderService Service() =>
            new(Db, Loglig, Runner ?? new DataCheckRunner(Db, []), Audit.Object, Impact, Finder(),
                new LogligMatchService(), NullLogger<LogligCardGenderService>.Instance, Gate, Recalc.Object, TimeSpan.Zero);
    }

    /// <summary>Карточка; <paramref name="swam"/> — на ней есть тот самый заплыв из нашей базы (50 вольным, 00:40.00).</summary>
    private static LogligPlayerCard Card(string name, int? year, string? gender, bool swam = true) =>
        new(name, year, gender, "ימק\"א", swam
            ? [new LogligResultRow("50 חופשי", "50", "freestyle", false, 25, "00:40.00", 40000, SwimDate, "ליגה")]
            : []);

    private static GenderImpact Serious(string code = GenderImpactRules.Record, string text = "появится рекорд 50 freestyle") =>
        new([new GenderImpactItem(code, text)]);

    /// <summary>
    /// Смешанный заплыв (compID 459): у каждого пловца строка без пола и открытая находка
    /// <c>results.no-gender</c>. Пловец — (id, имя, фамилия, год, пол, loglig-id, статус).
    /// </summary>
    private static async Task<World> SeedAsync(string name, Dictionary<int, LogligPlayerCard?> cards,
        params (int Id, string First, string Last, int Year, string? Gender, int? Loglig, string? Status)[] swimmers)
    {
        var db = CreateDb(name);
        var club = new Club { Name = "ימק\"א" };
        var style = new Style { Name = "freestyle" };
        var comp = new Competition { Name = "ליגה אזורית 1", Date = "21/11/2026", PoolType = "25m", OrgCompId = 459 };
        db.AddRange(club, style, comp);
        await db.SaveChangesAsync();

        foreach (var s in swimmers)
        {
            db.Swimmers.Add(new Swimmer
            {
                Id = s.Id, FirstName = s.First, LastName = s.Last, BirthYear = s.Year, Gender = s.Gender,
                LogligId = s.Loglig, LogligIdStatus = s.Status,
            });
            db.Results.Add(new ResultRecord
            {
                CompetitionId = comp.Id, SwimmerId = s.Id, ClubId = club.Id, StyleId = style.Id,
                Distance = "50", Gender = "", Position = s.Id % 10, EventCategory = "mix-shabbat",
                TimeOriginal = "00:40.00", TimeMillisecond = 40000, CompetitionDate = SwimDate,
            });
            db.DataCheckFindings.Add(new DataCheckFinding
            {
                CheckId = "results.no-gender", Severity = 1, EntityType = "Result", EntityId = s.Id,
                Message = $"{s.Last} {s.First}", FixKind = DataCheckFixKinds.SwimmerGender, FixEntityId = s.Id,
            });
        }
        await db.SaveChangesAsync();
        return new World { DbName = name, Db = db, Loglig = new FakeLoglig(cards), CompId = comp.Id };
    }

    [Fact]
    public async Task VerifiedSwimmerWithoutGender_GetsGenderFromCard_AndRowsFollow_WithAudit()
    {
        var w = await SeedAsync(nameof(VerifiedSwimmerWithoutGender_GetsGenderFromCard_AndRowsFollow_WithAudit),
            new() { [129763] = Card("סמואל מונייר", 1998, "M"), [327879] = Card("נועה גלסברג", 2017, "F") },
            (1, "סמואל", "מונייר", 1998, null, 129763, "Verified"),
            (2, "נועה", "גלסברג", 2017, "", 327879, "Verified"));

        var report = (await w.Service().FillFromFindingsAsync())!;

        Assert.Equal(2, report.Candidates);
        Assert.Equal(2, report.Set);
        Assert.Equal(2, report.Rows);
        Assert.Equal(2, report.Requests);
        Assert.Equal(0, report.Review);
        Assert.Equal("male", (await w.Db.Swimmers.FindAsync(1))!.Gender);
        Assert.Equal("female", (await w.Db.Swimmers.FindAsync(2))!.Gender);
        var rows = await w.Db.Results.AsNoTracking().OrderBy(r => r.SwimmerId).ToListAsync();
        Assert.Equal(["male", "female"], rows.Select(r => r.Gender));
        // Место и программа заплыва — напечатанное, запись пола их не трогает.
        Assert.Equal(new int?[] { 1, 2 }, rows.Select(r => r.Position));
        Assert.All(rows, r => Assert.Equal("mix-shabbat", r.EventCategory));

        w.Audit.Verify(a => a.LogAsync(LogligCardGenderRule.AuditAction, "Swimmer", "1",
            It.Is<string>(s => s.Contains("loglig #129763") && s.Contains("loglig card")),
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        w.Audit.Verify(a => a.LogAsync(LogligCardGenderRule.AuditAction, "Swimmer", "2",
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NameOrYearMismatch_NoGenderOnCard_UnavailableCard_AreNotWritten()
    {
        var w = await SeedAsync(nameof(NameOrYearMismatch_NoGenderOnCard_UnavailableCard_AreNotWritten),
            new()
            {
                [101] = Card("דניאל כהן", 2012, "M"),      // имя чужое
                [102] = Card("רוני כץ", 2015, "F"),        // год другой
                [103] = Card("אדל כון", 2019, null),       // пола на карточке нет
                [104] = null,                               // карточка не открылась
            },
            (1, "אלון", "רז", 2012, null, 101, "Verified"),
            (2, "רוני", "כץ", 2016, null, 102, "Verified"),
            (3, "אדל", "כון", 2019, null, 103, "Verified"),
            (4, "בר", "רמוס", 2016, null, 104, "Verified"));

        var report = (await w.Service().FillFromFindingsAsync())!;

        Assert.Equal(0, report.Set);
        Assert.Equal(2, report.Mismatch);
        Assert.Equal(1, report.CardWithoutGender);
        Assert.Equal(1, report.Failed);
        Assert.Equal(4, report.Notes.Count);
        Assert.All(await w.Db.Swimmers.AsNoTracking().ToListAsync(), s => Assert.Null(s.Gender));
        Assert.All(await w.Db.Results.AsNoTracking().ToListAsync(), r => Assert.Equal("", r.Gender));
        w.Audit.Verify(a => a.LogAsync(LogligCardGenderRule.AuditAction, It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Уже проставленный пол и неподтверждённый loglig-id — на loglig даже не идём.
    [Fact]
    public async Task KnownGender_AndUnverifiedId_AreNotAskedAndNotTouched()
    {
        var w = await SeedAsync(nameof(KnownGender_AndUnverifiedId_AreNotAskedAndNotTouched),
            new() { [201] = Card("נועה גלסברג", 2017, "M"), [202] = Card("אביה מזרחי", 2012, "F"), [203] = Card("ריטה עאסי", 2019, "F") },
            (1, "נועה", "גלסברג", 2017, "female", 201, "Verified"),   // у нас female, на loglig male
            (2, "אביה", "מזרחי", 2012, null, 202, "Suggested"),
            (3, "ריטה", "עאסי", 2019, null, null, null));

        var report = (await w.Service().FillFromFindingsAsync())!;

        Assert.Empty(w.Loglig.Calls);
        Assert.Equal(2, report.Candidates);
        Assert.Equal(2, report.NotVerified);
        Assert.Equal(1, report.AlreadyKnown);
        Assert.Equal(0, report.Set);
        Assert.Equal("female", (await w.Db.Swimmers.FindAsync(1))!.Gender);
    }

    [Fact]
    public async Task RequestCeiling_StopsNetwork_RestReportedAsOverLimit()
    {
        var total = LogligCardGenderRule.MaxRequestsPerImport + 5;
        var swimmers = Enumerable.Range(1, total)
            .Select(i => (i, "נועה", "גלסברג", 2017, (string?)null, (int?)(1000 + i), (string?)"Verified")).ToArray();
        var cards = swimmers.ToDictionary(s => s.Item6!.Value, _ => (LogligPlayerCard?)Card("נועה גלסברג", 2017, "F"));
        var w = await SeedAsync(nameof(RequestCeiling_StopsNetwork_RestReportedAsOverLimit), cards, swimmers);

        var report = await w.Service().FillAfterImportAsync(459);

        Assert.Equal(LogligCardGenderRule.MaxRequestsPerImport, w.Loglig.Calls.Count);
        Assert.Equal(LogligCardGenderRule.MaxRequestsPerImport, report.Requests);
        Assert.Equal(LogligCardGenderRule.MaxRequestsPerImport, report.Set);
        Assert.Equal(5, report.OverLimit);
        Assert.Contains("потолка запросов — 5", report.Message);
        Assert.Equal(5, await w.Db.Swimmers.CountAsync(s => s.Gender == null));
    }

    // Суточный ответ из общего места: в сеть второй раз не идём — но только если про заплывы уже известно.
    [Fact]
    public async Task FreshStoredAnswer_IsUsedWithoutNetwork_OnlyWhenIdentityIsKnown()
    {
        var w = await SeedAsync(nameof(FreshStoredAnswer_IsUsedWithoutNetwork_OnlyWhenIdentityIsKnown),
            new() { [302] = Card("אביה מזרחי", 2012, "F"), [304] = Card("רוני כץ", 2016, "F") },
            (1, "נועה", "גלסברג", 2017, null, 301, "Verified"),
            (2, "אביה", "מזרחי", 2012, null, 302, "Verified"),
            (3, "ריטה", "עאסי", 2019, null, 303, "Verified"),
            (4, "רוני", "כץ", 2016, null, 304, "Verified"));
        var at = DateTime.UtcNow.AddHours(-2);
        w.Db.SwimmerLogligClubs.AddRange(
            new SwimmerLogligClub
            {
                SwimmerId = 1, LogligId = 301, ClubName = "X", CheckedAt = at,
                CardName = "נועה גלסברג", CardBirthYear = 2017, Gender = "female", ResultsMatched = true,
            },
            // Ответ до Р125 (без имени) — для пола не годится: карточку открываем заново.
            new SwimmerLogligClub { SwimmerId = 3, LogligId = 303, ClubName = "X", CheckedAt = at },
            // Ответ записала сверка клуба: имя и пол есть, а заплывы не сверялись — открываем заново.
            new SwimmerLogligClub
            {
                SwimmerId = 4, LogligId = 304, ClubName = "X", CheckedAt = at,
                CardName = "רוני כץ", CardBirthYear = 2016, Gender = "female", ResultsMatched = null,
            });
        await w.Db.SaveChangesAsync();

        var report = await w.Service().FillDayAfterImportAsync(w.CompId);

        Assert.Equal([302, 303, 304], w.Loglig.Calls);
        Assert.Equal(1, report.FromCache);
        Assert.Equal(3, report.Set);
        Assert.Equal(1, report.Failed); // 303 не открылась
        Assert.Equal("female", (await w.Db.Swimmers.FindAsync(1))!.Gender);
        var stored = await w.Db.SwimmerLogligClubs.AsNoTracking().SingleAsync(x => x.SwimmerId == 2);
        Assert.Equal(("אביה מזרחי", 2012, "female", true),
            (stored.CardName, stored.CardBirthYear, stored.Gender, stored.ResultsMatched));
    }

    // Шаг импорта трогает только пловцов своего соревнования.
    [Fact]
    public async Task AfterImport_OnlySwimmersOfThatCompetition()
    {
        var w = await SeedAsync(nameof(AfterImport_OnlySwimmersOfThatCompetition),
            new() { [401] = Card("נועה גלסברג", 2017, "F"), [402] = Card("אביה מזרחי", 2012, "F") },
            (1, "נועה", "גלסברג", 2017, null, 401, "Verified"));
        w.Db.Swimmers.Add(new Swimmer { Id = 2, FirstName = "אביה", LastName = "מזרחי", BirthYear = 2012, LogligId = 402, LogligIdStatus = "Verified" });
        await w.Db.SaveChangesAsync();

        var report = await w.Service().FillAfterImportAsync(459);

        Assert.Equal([401], w.Loglig.Calls);
        Assert.Equal(1, report.Set);
        Assert.Null((await w.Db.Swimmers.FindAsync(2))!.Gender);
    }

    // ── Р126: тот же человек — нужен заплыв, а не только имя и год ───────────────────────

    [Fact]
    public async Task NameAndYearOnly_NoSwimOnCard_GoesToManualReview_NotWritten()
    {
        var w = await SeedAsync(nameof(NameAndYearOnly_NoSwimOnCard_GoesToManualReview_NotWritten),
            new() { [501] = Card("טל כהן", 2012, "M", swam: false) },
            (1, "טל", "כהן", 2012, null, 501, "Verified"));

        var report = (await w.Service().FillFromFindingsAsync())!;

        Assert.Equal(0, report.Set);
        Assert.Equal(1, report.Review);
        Assert.Equal(1, report.ReviewIdentity);
        Assert.Contains("ждут ручной проверки — 1, потому что личность не подтверждена: на карточке loglig нет ни одного заплыва пловца", report.Message);
        Assert.Null((await w.Db.Swimmers.FindAsync(1))!.Gender);

        var review = Assert.Single(await w.Finder().GetPendingAsync());
        Assert.Equal(GenderImpactRules.IdentityUnconfirmed, Assert.Single(review.Reasons).Code);
    }

    // Игрок loglig у строки пловца (Р122) — тот же id: это он плыл, карточке верим и без её списка заплывов.
    [Fact]
    public async Task RowPlayerOfTheSwim_ConfirmsIdentity_WithoutSwimOnCard()
    {
        var w = await SeedAsync(nameof(RowPlayerOfTheSwim_ConfirmsIdentity_WithoutSwimOnCard),
            new() { [502] = Card("טל כהן", 2012, "M", swam: false) },
            (1, "טל", "כהן", 2012, null, 502, "Verified"));
        (await w.Db.Results.SingleAsync()).LogligPlayerId = 502;
        await w.Db.SaveChangesAsync();

        var report = (await w.Service().FillFromFindingsAsync())!;

        Assert.Equal(1, report.Set);
        Assert.Equal(0, report.Review);
    }

    // ── Р126: серьёзные последствия — кнопка и шаг импорта пропускают, находка ждёт человека ──

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SeriousConsequences_AreSkipped_ByButtonAndImportStep(bool button)
    {
        var w = await SeedAsync(nameof(SeriousConsequences_AreSkipped_ByButtonAndImportStep) + button,
            new() { [601] = Card("נועה גלסברג", 2017, "F"), [602] = Card("אביה מזרחי", 2012, "F") },
            (1, "נועה", "גלסברג", 2017, null, 601, "Verified"),
            (2, "אביה", "מזרחי", 2012, null, 602, "Verified"));
        w.Impact.BySwimmer[1] = Serious();

        var report = button ? (await w.Service().FillFromFindingsAsync())! : await w.Service().FillAfterImportAsync(459);

        Assert.Equal(1, report.Set);
        Assert.Equal(1, report.Review);
        Assert.Equal(1, report.ReviewImpact);
        Assert.Equal(0, report.ReviewIdentity);
        Assert.Contains("проставлен 1", report.Message);
        Assert.Contains("ждут ручной проверки — 1", report.Message);
        Assert.Null((await w.Db.Swimmers.FindAsync(1))!.Gender);
        Assert.Equal("female", (await w.Db.Swimmers.FindAsync(2))!.Gender);
        w.Audit.Verify(a => a.LogAsync(LogligCardGenderRule.AuditAction, "Swimmer", "1", It.IsAny<string?>(),
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReviewFinding_ListsConsequences_AndHumanConfirmWrites_WithAuditAndRecalculation()
    {
        var w = await SeedAsync(nameof(ReviewFinding_ListsConsequences_AndHumanConfirmWrites_WithAuditAndRecalculation),
            new() { [701] = Card("נועה גלסברג", 2017, "F") },
            (1, "נועה", "גלסברג", 2017, null, 701, "Verified"));
        w.Impact.BySwimmer[1] = Serious(GenderImpactRules.Record, "появится рекорд 50 freestyle female — 00:40.00");
        await w.Service().FillFromFindingsAsync();

        // Находка Health: простыми словами, с перечнем и своей кнопкой.
        var item = Assert.Single((await new GenderFromLogligReviewCheck(w.Finder()).RunAsync()).Items);
        Assert.Contains("loglig говорит, что это женщина", item.Message);
        Assert.Contains("Если проставить, изменится: появится рекорд 50 freestyle female", item.Details);
        Assert.Equal(DataCheckFixKinds.SwimmerGenderLogligReview, item.FixKind);
        Assert.Equal(1, item.FixEntityId);

        var finding = new DataCheckFinding
        {
            CheckId = "swimmers.gender-from-loglig-review", Severity = 1, EntityType = "Swimmer", EntityId = 1,
            Message = item.Message, FixKind = item.FixKind, FixEntityId = 1,
        };
        w.Db.DataCheckFindings.Add(finding);
        await w.Db.SaveChangesAsync();

        // Чужая находка «Проставить» не запускает.
        var other = await w.Db.DataCheckFindings.FirstAsync(f => f.FixKind == DataCheckFixKinds.SwimmerGender);
        Assert.Null(await w.Service().ConfirmFindingAsync(other.Id));

        var result = (await w.Service().ConfirmFindingAsync(finding.Id))!;

        Assert.Equal(("female", 1, 1), (result.Gender, result.Rows, result.Recalculated));
        Assert.Equal("female", (await w.Db.Swimmers.AsNoTracking().SingleAsync()).Gender);
        Assert.Equal("female", (await w.Db.Results.AsNoTracking().SingleAsync()).Gender);
        w.Audit.Verify(a => a.LogAsync(LogligCardGenderRule.ConfirmAuditAction, "Swimmer", "1",
            It.Is<string>(s => s.Contains("подтвердил человек")), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        w.Recalc.Verify(r => r.RecalculateCompetitionAsync(w.CompId, It.IsAny<CancellationToken>()), Times.Once);
        // Проставлено — ждать больше нечего, второй раз не проставить.
        Assert.Empty(await w.Finder().GetPendingAsync());
        Assert.Null(await w.Service().ConfirmFindingAsync(finding.Id));
    }

    // ── Ревью: условная запись, замок, время, сбой на пловце ──────────────────────────────

    // Пока шёл расчёт, пол проставил импорт из шапки протокола — его не перезаписываем, строки не трогаем.
    [Fact]
    public async Task GenderSetMeanwhile_ByImport_IsNotOverwritten()
    {
        var name = nameof(GenderSetMeanwhile_ByImport_IsNotOverwritten);
        var w = await SeedAsync(name, new() { [801] = Card("נועה גלסברג", 2017, "F") },
            (1, "נועה", "גלסברג", 2017, null, 801, "Verified"));
        w.Impact.OnAssess = async id =>
        {
            await using var other = CreateDb(name);
            (await other.Swimmers.SingleAsync(s => s.Id == id)).Gender = "male";
            await other.SaveChangesAsync();
        };

        var report = (await w.Service().FillFromFindingsAsync())!;

        Assert.Equal(0, report.Set);
        await using var check = CreateDb(name);
        Assert.Equal("male", (await check.Swimmers.SingleAsync()).Gender);
        Assert.Equal("", (await check.Results.SingleAsync()).Gender);
        w.Audit.Verify(a => a.LogAsync(LogligCardGenderRule.AuditAction, It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SecondRun_WhileFirstIsGoing_IsRefused()
    {
        var w = await SeedAsync(nameof(SecondRun_WhileFirstIsGoing_IsRefused),
            new() { [901] = Card("נועה גלסברג", 2017, "F") },
            (1, "נועה", "גלסברג", 2017, null, 901, "Verified"));

        await w.Gate.Semaphore.WaitAsync();
        try
        {
            Assert.Null(await w.Service().FillFromFindingsAsync());
            Assert.Empty(w.Loglig.Calls);
        }
        finally { w.Gate.Semaphore.Release(); }

        Assert.NotNull(await w.Service().FillFromFindingsAsync());
    }

    // Время кнопки вышло посреди прогона: не исключение, а честный итог «дошли до N из M».
    [Fact]
    public async Task TimeRunsOut_ReturnsHonestPartialReport()
    {
        var w = await SeedAsync(nameof(TimeRunsOut_ReturnsHonestPartialReport),
            new() { [1001] = Card("נועה גלסברג", 2017, "F"), [1002] = Card("אביה מזרחי", 2012, "F"), [1003] = Card("ריטה עאסי", 2019, "F") },
            (1, "נועה", "גלסברג", 2017, null, 1001, "Verified"),
            (2, "אביה", "מזרחי", 2012, null, 1002, "Verified"),
            (3, "ריטה", "עאסי", 2019, null, 1003, "Verified"));
        using var cts = new CancellationTokenSource();
        w.Impact.OnAssess = id => { if (id == 1) cts.Cancel(); return Task.CompletedTask; };

        var report = (await w.Service().FillFromFindingsAsync(cts.Token))!;

        Assert.True(report.Interrupted);
        Assert.True(report.Reached < report.Candidates);
        Assert.Contains($"дошли до {report.Reached} из 3", report.Message);
        Assert.True(await w.Db.Swimmers.AsNoTracking().CountAsync(s => s.Gender == null) >= 2);
    }

    // Сбой базы на одном пловце остальных не срывает.
    [Fact]
    public async Task DatabaseFailureOnOneSwimmer_DoesNotStopTheRest()
    {
        var w = await SeedAsync(nameof(DatabaseFailureOnOneSwimmer_DoesNotStopTheRest),
            new() { [1101] = Card("נועה גלסברג", 2017, "F"), [1102] = Card("אביה מזרחי", 2012, "F") },
            (1, "נועה", "גלסברג", 2017, null, 1101, "Verified"),
            (2, "אביה", "מזרחי", 2012, null, 1102, "Verified"));
        var real = new DataCheckRunner(w.Db, []);
        var runner = new Mock<IDataCheckRunner>();
        runner.Setup(r => r.SetSwimmerGenderIfEmptyAsync(1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("deadlock"));
        runner.Setup(r => r.SetSwimmerGenderIfEmptyAsync(2, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((int id, string g, CancellationToken ct) => real.SetSwimmerGenderIfEmptyAsync(id, g, ct));
        w.Runner = runner.Object;

        var report = (await w.Service().FillFromFindingsAsync())!;

        Assert.Equal(1, report.DbFailed);
        Assert.Equal(1, report.Set);
        Assert.Contains("сбой записи — 1", report.Message);
        Assert.Equal("female", (await w.Db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 2)).Gender);
    }

    // ── Проверка «пол пловца против карточки loglig»: показывает, не чинит ──────────────

    [Fact]
    public async Task GenderVsLogligCheck_ShowsDisagreement_OnlyForSamePerson()
    {
        var db = CreateDb(nameof(GenderVsLogligCheck_ShowsDisagreement_OnlyForSamePerson));
        db.Swimmers.AddRange(
            new Swimmer { Id = 1, FirstName = "נועה", LastName = "גלסברג", BirthYear = 2017, Gender = "female", LogligId = 501, LogligIdStatus = "Verified" },
            new Swimmer { Id = 2, FirstName = "אביה", LastName = "מזרחי", BirthYear = 2012, Gender = "female", LogligId = 502, LogligIdStatus = "Verified" },
            new Swimmer { Id = 3, FirstName = "ריטה", LastName = "עאסי", BirthYear = 2019, Gender = "female", LogligId = 503, LogligIdStatus = "Verified" },
            new Swimmer { Id = 4, FirstName = "רוני", LastName = "כץ", BirthYear = 2016, Gender = null, LogligId = 504, LogligIdStatus = "Verified" });
        var at = DateTime.UtcNow;
        db.SwimmerLogligClubs.AddRange(
            new SwimmerLogligClub { SwimmerId = 1, LogligId = 501, CheckedAt = at, CardName = "נועה גלסברג", CardBirthYear = 2017, Gender = "male" },
            new SwimmerLogligClub { SwimmerId = 2, LogligId = 502, CheckedAt = at, CardName = "אביה מזרחי", CardBirthYear = 2012, Gender = "female" },
            // Под id чужая карточка — это не «пол разный», а другая болезнь.
            new SwimmerLogligClub { SwimmerId = 3, LogligId = 503, CheckedAt = at, CardName = "דניאל כהן", CardBirthYear = 2019, Gender = "male" },
            new SwimmerLogligClub { SwimmerId = 4, LogligId = 504, CheckedAt = at, CardName = "רוני כץ", CardBirthYear = 2016, Gender = "male" });
        await db.SaveChangesAsync();

        var outcome = await new GenderVsLogligCheck(db).RunAsync();

        var item = Assert.Single(outcome.Items);
        Assert.Equal(1, item.EntityId);
        Assert.Contains("у нас female, на loglig male", item.Message);
        Assert.Null(item.FixKind); // кнопок исправления нет — решает человек
        Assert.Equal("female", (await db.Swimmers.FindAsync(1))!.Gender);
    }

    // ── Кнопки Health ───────────────────────────────────────────────────────────────

    [Fact]
    public void Buttons_AreAdminOnly_Post_WithAntiforgery()
    {
        var type = typeof(DataChecksAdminController);
        Assert.Equal("Admin", type.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.NotNull(type.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>());

        foreach (var (name, route) in new[]
                 {
                     (nameof(DataChecksAdminController.FixGenderFromLoglig), "fix-gender-from-loglig"),
                     (nameof(DataChecksAdminController.ConfirmGenderFromLoglig), "{id:int}/confirm-gender-from-loglig"),
                 })
        {
            var method = type.GetMethod(name)!;
            Assert.Equal(route, method.GetCustomAttribute<HttpPostAttribute>()!.Template);
            Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.Null(method.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>());
        }
    }

    private static LogligCardGenderReport Report(bool interrupted = false) =>
        new(12, 9, 10, 1, 1, 1, 0, 0, 11, 1, 0, "итог", ["заметка"], Review: 2, Interrupted: interrupted, Reached: 9);

    [Fact]
    public async Task Button_ReturnsReport_AndAuditsTheClick_EvenWhenInterrupted()
    {
        var service = new Mock<ILogligCardGenderService>();
        service.Setup(s => s.FillFromFindingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Report(interrupted: true));
        var audit = new Mock<IAdminAuditService>();

        var result = await new DataChecksAdminController(Mock.Of<IDataCheckRunner>(), audit.Object, service.Object)
            .FixGenderFromLoglig();

        Assert.True(Assert.IsType<LogligCardGenderReport>(Assert.IsType<OkObjectResult>(result).Value).Interrupted);
        audit.Verify(a => a.LogAsync("datacheck.fix-gender-from-loglig", "DataCheckFinding", null,
            It.Is<string>(s => s.Contains("итог")), It.IsAny<object>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Button_WhileAnotherRunIsGoing_Returns409_WithoutAudit()
    {
        var service = new Mock<ILogligCardGenderService>();
        service.Setup(s => s.FillFromFindingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync((LogligCardGenderReport?)null);
        var audit = new Mock<IAdminAuditService>();

        var result = await new DataChecksAdminController(Mock.Of<IDataCheckRunner>(), audit.Object, service.Object)
            .FixGenderFromLoglig();

        Assert.Equal(409, Assert.IsType<ConflictObjectResult>(result).StatusCode);
        audit.VerifyNoOtherCalls();
    }

    // Итог нажатия не пропадает с тостом (Влад, 25.11.2026): значок ⓘ берёт последнюю строку аудита.
    [Fact]
    public async Task LastButtonRun_IsTheLatestAuditLine_WithItsNotes()
    {
        var w = await SeedAsync(nameof(LastButtonRun_IsTheLatestAuditLine_WithItsNotes), new());
        Assert.Null(await w.Service().LastButtonRunAsync());

        w.Db.AdminAudits.AddRange(
            new AdminAudit { Action = LogligCardGenderRule.ButtonAuditAction, EntityType = "DataCheckFinding", ActorName = "vlad",
                Summary = "старый", CreatedAt = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc) },
            new AdminAudit { Action = LogligCardGenderRule.ButtonAuditAction, EntityType = "DataCheckFinding", ActorName = "vlad",
                Summary = "свежий", DetailsJson = """{"Review":1,"Notes":["#1 טל כהן: ждёт ручной проверки"]}""",
                CreatedAt = new DateTime(2026, 11, 25, 14, 0, 0, DateTimeKind.Utc) },
            new AdminAudit { Action = "datacheck.accept", EntityType = "DataCheckFinding", ActorName = "vlad",
                Summary = "чужое", CreatedAt = new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Utc) });
        await w.Db.SaveChangesAsync();

        var run = (await w.Service().LastButtonRunAsync())!;

        Assert.Equal("свежий", run.Message);
        Assert.Equal("vlad", run.By);
        Assert.Equal(["#1 טל כהן: ждёт ручной проверки"], run.Notes);
    }

    [Fact]
    public async Task LastButtonRun_Endpoint_204WhenNeverPressed_OkOtherwise()
    {
        var service = new Mock<ILogligCardGenderService>();
        var controller = new DataChecksAdminController(Mock.Of<IDataCheckRunner>(), Mock.Of<IAdminAuditService>(), service.Object);
        Assert.Equal("fix-gender-from-loglig/last", typeof(DataChecksAdminController)
            .GetMethod(nameof(DataChecksAdminController.LastFixGenderFromLoglig))!.GetCustomAttribute<HttpGetAttribute>()!.Template);

        Assert.IsType<NoContentResult>(await controller.LastFixGenderFromLoglig(CancellationToken.None));

        service.Setup(s => s.LastButtonRunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogligGenderButtonRun(DateTime.UtcNow, "vlad", "итог", []));
        Assert.Equal("итог", Assert.IsType<LogligGenderButtonRun>(
            Assert.IsType<OkObjectResult>(await controller.LastFixGenderFromLoglig(CancellationToken.None)).Value).Message);
    }

    [Fact]
    public async Task Button_WhenRunCrashes_StillWritesTheAuditLine()
    {
        var service = new Mock<ILogligCardGenderService>();
        service.Setup(s => s.FillFromFindingsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("база легла"));
        var audit = new Mock<IAdminAuditService>();
        var controller = new DataChecksAdminController(Mock.Of<IDataCheckRunner>(), audit.Object, service.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.FixGenderFromLoglig());

        audit.Verify(a => a.LogAsync("datacheck.fix-gender-from-loglig", "DataCheckFinding", null,
            It.Is<string>(s => s.Contains("прогон сорвался") && s.Contains("база легла")), It.IsAny<object>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ConfirmButton_NothingToWrite_Is400()
    {
        var service = new Mock<ILogligCardGenderService>();
        service.Setup(s => s.ConfirmFindingAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync((LogligGenderConfirmResult?)null);
        var controller = new DataChecksAdminController(Mock.Of<IDataCheckRunner>(), Mock.Of<IAdminAuditService>(), service.Object);

        Assert.IsType<BadRequestObjectResult>(await controller.ConfirmGenderFromLoglig(7, CancellationToken.None));
    }
}
