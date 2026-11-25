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
/// Р125: пол пловца с карточки loglig — сервис (выборка, суточный ответ, потолок запросов, запись
/// тем же действием, что кнопки ♂/♀, аудит), проверка расхождения и кнопка Health.
/// Сеть — подставной <see cref="ILogligClient"/>.
/// </summary>
public class LogligCardGenderServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class FakeLoglig(Dictionary<int, LogligPlayerCard?> cards) : ILogligClient
    {
        public List<int> Calls { get; } = [];

        public Task<LogligPlayerCard?> GetPlayerCardAsync(int logligId, int? seasonId = null, CancellationToken ct = default)
        {
            Calls.Add(logligId);
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

    private sealed class World
    {
        public required SwimmDbContext Db;
        public required FakeLoglig Loglig;
        public readonly Mock<IAdminAuditService> Audit = new();
        public int CompId;

        public LogligCardGenderService Service() =>
            new(Db, Loglig, new DataCheckRunner(Db, []), Audit.Object,
                NullLogger<LogligCardGenderService>.Instance, TimeSpan.Zero);
    }

    private static LogligPlayerCard Card(string name, int? year, string? gender) => new(name, year, gender, "ימק\"א", []);

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
            var row = new ResultRecord
            {
                CompetitionId = comp.Id, SwimmerId = s.Id, ClubId = club.Id, StyleId = style.Id,
                Distance = "50", Gender = "", Position = s.Id % 10, EventCategory = "mix-shabbat",
                TimeOriginal = "00:40.00", CompetitionDate = new DateTime(2026, 11, 21),
            };
            db.Results.Add(row);
            db.DataCheckFindings.Add(new DataCheckFinding
            {
                CheckId = "results.no-gender", Severity = 1, EntityType = "Result", EntityId = s.Id,
                Message = $"{s.Last} {s.First}", FixKind = DataCheckFixKinds.SwimmerGender, FixEntityId = s.Id,
            });
        }
        await db.SaveChangesAsync();
        return new World { Db = db, Loglig = new FakeLoglig(cards), CompId = comp.Id };
    }

    [Fact]
    public async Task VerifiedSwimmerWithoutGender_GetsGenderFromCard_AndRowsFollow_WithAudit()
    {
        var w = await SeedAsync(nameof(VerifiedSwimmerWithoutGender_GetsGenderFromCard_AndRowsFollow_WithAudit),
            new() { [129763] = Card("סמואל מונייר", 1998, "M"), [327879] = Card("נועה גלסברג", 2017, "F") },
            (1, "סמואל", "מונייר", 1998, null, 129763, "Verified"),
            (2, "נועה", "גלסברג", 2017, "", 327879, "Verified"));

        var report = await w.Service().FillFromFindingsAsync();

        Assert.Equal(2, report.Candidates);
        Assert.Equal(2, report.Set);
        Assert.Equal(2, report.Rows);
        Assert.Equal(2, report.Requests);
        Assert.Equal("male", (await w.Db.Swimmers.FindAsync(1))!.Gender);
        Assert.Equal("female", (await w.Db.Swimmers.FindAsync(2))!.Gender);
        var rows = await w.Db.Results.AsNoTracking().OrderBy(r => r.SwimmerId).ToListAsync();
        Assert.Equal(["male", "female"], rows.Select(r => r.Gender));
        // Место и программа заплыва — напечатанное, запись пола их не трогает.
        Assert.Equal([1, 2], rows.Select(r => r.Position));
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

        var report = await w.Service().FillFromFindingsAsync();

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

        var report = await w.Service().FillFromFindingsAsync();

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

    // Суточный ответ из общего места (его пишет и сверка клуба): в сеть второй раз не идём.
    [Fact]
    public async Task FreshStoredAnswer_IsUsedWithoutNetwork_AndFetchedCardIsStored()
    {
        var w = await SeedAsync(nameof(FreshStoredAnswer_IsUsedWithoutNetwork_AndFetchedCardIsStored),
            new() { [302] = Card("אביה מזרחי", 2012, "F") },
            (1, "נועה", "גלסברג", 2017, null, 301, "Verified"),
            (2, "אביה", "מזרחי", 2012, null, 302, "Verified"),
            (3, "ריטה", "עאסי", 2019, null, 303, "Verified"));
        w.Db.SwimmerLogligClubs.AddRange(
            new SwimmerLogligClub
            {
                SwimmerId = 1, LogligId = 301, ClubName = "X", CheckedAt = DateTime.UtcNow.AddHours(-2),
                CardName = "נועה גלסברג", CardBirthYear = 2017, Gender = "female",
            },
            // Ответ до Р125 (без имени) — для пола не годится: карточку открываем заново.
            new SwimmerLogligClub { SwimmerId = 3, LogligId = 303, ClubName = "X", CheckedAt = DateTime.UtcNow.AddHours(-2) });
        await w.Db.SaveChangesAsync();

        var report = await w.Service().FillDayAfterImportAsync(w.CompId);

        Assert.Equal([302, 303], w.Loglig.Calls);
        Assert.Equal(1, report.FromCache);
        Assert.Equal(2, report.Set);
        Assert.Equal(1, report.Failed); // 303 не открылась
        Assert.Equal("female", (await w.Db.Swimmers.FindAsync(1))!.Gender);
        var stored = await w.Db.SwimmerLogligClubs.AsNoTracking().SingleAsync(x => x.SwimmerId == 2);
        Assert.Equal(("אביה מזרחי", 2012, "female"), (stored.CardName, stored.CardBirthYear, stored.Gender));
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

    // ── Кнопка Health ───────────────────────────────────────────────────────────────

    [Fact]
    public void Button_IsAdminOnly_Post_WithAntiforgery()
    {
        var type = typeof(DataChecksAdminController);
        Assert.Equal("Admin", type.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.NotNull(type.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>());

        var method = type.GetMethod(nameof(DataChecksAdminController.FixGenderFromLoglig))!;
        Assert.Equal("fix-gender-from-loglig", method.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(method.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>());
    }

    [Fact]
    public async Task Button_RunsOverFindings_ReturnsReport_AndAuditsTheClick()
    {
        var report = new LogligCardGenderReport(12, 9, 10, 1, 1, 1, 0, 0, 11, 1, 0, "итог", ["заметка"]);
        var service = new Mock<ILogligCardGenderService>();
        service.Setup(s => s.FillFromFindingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(report);
        var audit = new Mock<IAdminAuditService>();

        var controller = new DataChecksAdminController(Mock.Of<IDataCheckRunner>(), audit.Object, service.Object);
        var result = await controller.FixGenderFromLoglig(CancellationToken.None);

        Assert.Same(report, Assert.IsType<OkObjectResult>(result).Value);
        audit.Verify(a => a.LogAsync("datacheck.fix-gender-from-loglig", "DataCheckFinding", null,
            It.Is<string>(s => s.Contains("итог")), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
