using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Р124: «Затянуть этот день» насквозь на InMemory — compID 6611: пятница (loglig 11588) в базе,
/// суббота — соседнее соревнование 11590. Превью, перепроверки сервера при «Импортировать» и шаг
/// после импорта (сбор в событие). Postgres-пути сбора (переименование под уникальным индексом) —
/// <c>SiblingDayPullPgTests</c>.
/// </summary>
[Collection(nameof(SiblingDayPullServiceTests))] // общий замок «один импорт дня на старт» — статический
public class SiblingDayPullServiceTests
{
    private const string FriTitle = "תחרות חלופית ל\"מילניום\" יום שישי 6.12.24";
    private const string BoundTitle = FriTitle + " - 06/12/2024";
    private const string SatTitle = "תחרות חלופית ל\"מילניום\" יום שבת 7.12.24 - 07/12/2024";
    private static readonly DateOnly Sat = new(2024, 12, 7);

    private sealed class Ctx
    {
        public required SwimmDbContext Db;
        public required int DiscoveryId;
        public required int FridayId;
        public readonly Mock<IProgrammeCheckService> Check = new();
        public readonly Mock<ICompetitionDiscoveryProvider> Loglig = new();
        public readonly Mock<ICompetitionDayJoinService> Join = new();
        public readonly Mock<IAdminAuditService> Audit = new();
        public readonly BulkPullServiceTests.FakeQueue Queue = new();
        public readonly IMemoryCache Cache = new MemoryCache(new MemoryCacheOptions());
        public Mock<IDiscoveryPreviewService> Previews = new();
        public SiblingDayPullService Service(SwimmDbContext? db = null) => new(db ?? Db, Check.Object, Loglig.Object, Previews.Object, Queue,
            Join.Object, Audit.Object, NullLogger<SiblingDayPullService>.Instance);
    }

    private static string Json(params (string Last, string First, int Heat, int Lane, string Style)[] rows) =>
        JsonSerializer.Serialize(new
        {
            results = rows.Select(r => new Dictionary<string, object?>
            {
                ["competition"] = SatTitle[..SatTitle.IndexOf(" - ", StringComparison.Ordinal)], ["date"] = "07/12/2024",
                ["event_style_name"] = r.Style, ["event_style_len"] = "50", ["heat"] = r.Heat, ["lane"] = r.Lane,
                ["last_name"] = r.Last, ["first_name"] = r.First, ["birth_year"] = 2010, ["club"] = "הפועל",
            }),
        });

    private static SwimmDbContext Open(string name) => new(new DbContextOptionsBuilder<SwimmDbContext>()
        .UseInMemoryDatabase(name).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static async Task<Ctx> SeedAsync(string name, Action<Competition>? day = null)
    {
        var db = Open(name);
        var free = new Style { Name = "freestyle" };
        var swimmer = new Swimmer { FirstName = "נועה", LastName = "לוי", BirthYear = 2010 };
        var club = new Club { Name = "הפועל" };
        var cat = new Category { Key = "results-8-99", Name = "8-99" };
        var fri = new Competition
        {
            Name = FriTitle, Date = "06/12/2024", PoolType = "25m", OrgCompId = 6611,
            IsAward = true, ClubPointsDisabled = true, PointRuleSwimmersId = 3, PointRuleClubsId = 5,
        };
        day?.Invoke(fri);
        var disc = new DiscoveredCompetition
        {
            OrgCompId = 6611, LogligId = 11588, Name = "תחרות חלופית למילניום",
            DateStart = new DateTime(2024, 12, 6), DateEnd = new DateTime(2024, 12, 7), Status = "imported",
        };
        db.AddRange(free, swimmer, club, cat, fri, disc);
        await db.SaveChangesAsync();
        db.CategoryCompetitions.Add(new CategoryCompetition { CategoryId = cat.Id, CompetitionId = fri.Id, DisplayOrder = 1 });
        db.Results.Add(new ResultRecord
        {
            CompetitionId = fri.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = free.Id, Distance = "50",
            Gender = "female", Heat = 3, Lane = 4, CompetitionDate = new DateTime(2024, 12, 6),
        });
        await db.SaveChangesAsync();

        var c = new Ctx { Db = db, DiscoveryId = disc.Id, FridayId = fri.Id };
        c.Check.Setup(s => s.CheckAsync(disc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Verdict(11590));
        c.Loglig.Setup(l => l.FetchCompetitionSummaryAsync(11588, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogligCompetitionSummaryDto(11588, BoundTitle, [new DateOnly(2024, 12, 6)]));
        c.Loglig.Setup(l => l.FetchCompetitionSummaryAsync(11590, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogligCompetitionSummaryDto(11590, SatTitle, [Sat]));
        return c;
    }

    private static ProgrammeCheckVerdict Verdict(params int[] candidates) => new(ProgrammeVerdictKind.OnlyKnownDays, "h", [], "hint",
        null, null, false, Candidates: candidates.Select(id => new ProgrammeNeighbour(id, SatTitle, [Sat])).ToList());

    /// <summary>Разбор соседа: и превью (PreviewSiblingAsync), и отложенная запись (GetEntry).</summary>
    private static Guid ParsedSaturday(Ctx c, string json, string? regulationPool = "25m")
    {
        var parsed = new ParsedCompetition
        {
            Format = "IsrOrg", ResultsJson = json, ResultCount = 1,
            Competitions = [new ParsedCompetitionSummary(SatTitle[..SatTitle.IndexOf(" - ", StringComparison.Ordinal)], "07/12/2024", 1)],
        };
        var id = Guid.NewGuid();
        var flags = new CompetitionFlagSuggestion(false, false, false, false, regulationPool, null,
            new Dictionary<string, string>(), ["results-main"]);
        c.Previews.Setup(p => p.PreviewSiblingAsync(c.DiscoveryId, 11590, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DiscoveryPreviewResult(id, parsed, ["he"], null, [], null, null, flags));
        c.Previews.Setup(p => p.GetEntry(id))
            .Returns(new DiscoveryPreviewEntry(parsed, "isrorg-6611-loglig-11590-he.pdf", c.DiscoveryId, null, SiblingLogligId: 11590));
        c.Previews.SetupGet(p => p.EntryLifetime).Returns(TimeSpan.FromMinutes(60));
        return id;
    }

    private static string OtherSwims() => Json(("כהן", "דנה", 1, 2, "freestyle"), ("לוי", "נועה", 3, 4, "backstroke"));

    private static SiblingDayImportRequest Request(Guid previewId, string? pool = "25m", string? eventName = "תחרות חלופית ל\"מילניום\"") =>
        new(previewId, 11590, ["results-8-99"], eventName, pool, true, false, false, true, 5);

    // ── превью ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_DefaultsFromExistingDay_EventNameWithoutWeekday()
    {
        var c = await SeedAsync(nameof(Preview_DefaultsFromExistingDay_EventNameWithoutWeekday));
        ParsedSaturday(c, OtherSwims(), regulationPool: null);

        var r = await c.Service().PreviewAsync(c.DiscoveryId, 11590);

        Assert.Null(r.Refusal);
        var f = r.Preview!.Flags!;
        Assert.True(f.IsAward);
        Assert.True(f.ClubPointsDisabled);
        Assert.Equal("25m", f.PoolType);
        Assert.Equal(["results-8-99"], f.CategoryKeys);
        Assert.Contains("как у лежащего дня", f.Reasons["isAward"]);
        Assert.Equal("תחרות חלופית ל\"מילניום\"", r.Context!.SuggestedEventName);
        Assert.Null(r.Context.EventId);
        Assert.Equal(5, r.Context.PointRuleClubsId);
        Assert.False(r.Context.PoolConflict);
    }

    [Fact]
    public async Task Preview_PoolDiffersFromRegulation_ExistingDayPool_SaidPlainly()
    {
        var c = await SeedAsync(nameof(Preview_PoolDiffersFromRegulation_ExistingDayPool_SaidPlainly));
        ParsedSaturday(c, OtherSwims(), regulationPool: "50m");

        var r = await c.Service().PreviewAsync(c.DiscoveryId, 11590);

        Assert.True(r.Context!.PoolConflict);
        Assert.Equal("25m", r.Preview!.Flags!.PoolType); // сервер примет только длину лежащего дня
        Assert.Equal("⚠ У пятницы в базе 25 м, а в регламенте субботы 50 м. Мы добавим субботу только с тем же бассейном, что у пятницы. "
            + "Если у пятницы бассейн записан неверно — сначала исправьте его через «Изменить».", r.Context.PoolConflictNote);
    }

    [Fact]
    public async Task Preview_TwoCandidates_Refused_NoParse()
    {
        var c = await SeedAsync(nameof(Preview_TwoCandidates_Refused_NoParse));
        c.Check.Setup(s => s.CheckAsync(c.DiscoveryId, It.IsAny<CancellationToken>())).ReturnsAsync(Verdict(11590, 11591));

        var r = await c.Service().PreviewAsync(c.DiscoveryId, 11590);

        Assert.Contains("один подходящий день", r.Refusal);
        c.Previews.Verify(p => p.PreviewSiblingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── запись: сервер перепроверяет ─────────────────────────────────────────

    [Fact]
    public async Task Import_SingleDay_QueuesWithoutCompId_TargetForJoin_Audited()
    {
        SiblingDayPullService.ResetPendingForTests();
        var c = await SeedAsync(nameof(Import_SingleDay_QueuesWithoutCompId_TargetForJoin_Audited));
        var id = ParsedSaturday(c, OtherSwims());

        var r = await c.Service().ImportAsync(c.DiscoveryId, Request(id), "vlad@test");

        Assert.Null(r.Refusal);
        var job = Assert.Single(c.Queue.Jobs);
        var o = job.Options!;
        Assert.Null(o.EventId);              // событие соберёт фон
        Assert.Equal("25m", o.PoolType);
        Assert.Equal(3, o.PointRuleSwimmersId); // как у лежащего дня
        Assert.Equal(11590, o.SiblingDay!.LogligId);
        Assert.Equal(c.FridayId, o.SiblingDay.ExistingCompetitionId);
        Assert.Equal("תחרות חלופית ל\"מילניום\"", o.SiblingDay.NewEventName);
        Assert.Empty(c.Queue.ByRow); // без discoveredId — статус записи федерации не трогаем
        c.Audit.Verify(a => a.LogAsync(SiblingDayPullService.AuditAction, "Competition", c.FridayId.ToString(),
            It.Is<string>(s => s.Contains("11590")), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        c.Previews.Verify(p => p.RemoveEntry(id), Times.Once);
    }

    [Fact]
    public async Task Import_ExistingEvent_GoesIntoThatEvent()
    {
        SiblingDayPullService.ResetPendingForTests();
        var c = await SeedAsync(nameof(Import_ExistingEvent_GoesIntoThatEvent));
        var ev = new CompetitionEvent { Name = "תחרות חלופית ל\"מילניום\"", OrgCompId = 6611 };
        c.Db.CompetitionEvents.Add(ev);
        await c.Db.SaveChangesAsync();
        var fri = c.Db.Competitions.Single();
        fri.EventId = ev.Id; fri.DayNumber = 1; fri.OrgCompId = 6611;
        await c.Db.SaveChangesAsync();
        var id = ParsedSaturday(c, OtherSwims());

        var r = await c.Service().ImportAsync(c.DiscoveryId, Request(id, eventName: null), "vlad@test");

        Assert.Null(r.Refusal);
        var o = Assert.Single(c.Queue.Jobs).Options!;
        Assert.Equal(ev.Id, o.EventId);
        Assert.Null(o.SiblingDay!.NewEventName);
    }

    [Fact]
    public async Task Import_CandidateStoppedMatching_Refused()
    {
        SiblingDayPullService.ResetPendingForTests();
        var c = await SeedAsync(nameof(Import_CandidateStoppedMatching_Refused));
        var id = ParsedSaturday(c, OtherSwims());
        c.Loglig.Setup(l => l.FetchCompetitionSummaryAsync(11590, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogligCompetitionSummaryDto(11590, "אליפות אחרת - 07/12/2024", [Sat]));

        var r = await c.Service().ImportAsync(c.DiscoveryId, Request(id), "vlad@test");

        Assert.Contains("другое соревнование", r.Refusal);
        Assert.Empty(c.Queue.Jobs);
    }

    [Fact]
    public async Task Import_DayAlreadyInDb_Refused()
    {
        SiblingDayPullService.ResetPendingForTests();
        var c = await SeedAsync(nameof(Import_DayAlreadyInDb_Refused));
        var id = ParsedSaturday(c, OtherSwims());
        // Субботу уже затянули руками отдельным соревнованием (как 1899 до сбора в событие).
        c.Db.Competitions.Add(new Competition
        {
            Name = SatTitle[..SatTitle.IndexOf(" - ", StringComparison.Ordinal)], Date = "07/12/2024", PoolType = "25m",
        });
        await c.Db.SaveChangesAsync();

        var r = await c.Service().ImportAsync(c.DiscoveryId, Request(id), "vlad@test");

        Assert.Contains("у нас уже есть", r.Refusal);
        Assert.Empty(c.Queue.Jobs);
    }

    [Fact]
    public async Task Import_SameProtocolOverlap_Refused()
    {
        SiblingDayPullService.ResetPendingForTests();
        var c = await SeedAsync(nameof(Import_SameProtocolOverlap_Refused));
        var swimmers = new[] { ("כהן", "דנה"), ("לוי", "רוני"), ("מזרחי", "טל") };
        var free = c.Db.Styles.First();
        foreach (var (i, (last, first)) in swimmers.Index())
        {
            var s = new Swimmer { LastName = last, FirstName = first, BirthYear = 2010 };
            c.Db.Swimmers.Add(s);
            await c.Db.SaveChangesAsync();
            c.Db.Results.Add(new ResultRecord
            {
                CompetitionId = c.FridayId, SwimmerId = s.Id, ClubId = c.Db.Clubs.First().Id, StyleId = free.Id,
                Distance = "50", Gender = "female", Heat = 5, Lane = i + 1, CompetitionDate = new DateTime(2024, 12, 6),
            });
        }
        await c.Db.SaveChangesAsync();
        var id = ParsedSaturday(c, Json(swimmers.Select((s, i) => (s.Item1, s.Item2, 5, i + 1, "freestyle")).ToArray()));

        var r = await c.Service().ImportAsync(c.DiscoveryId, Request(id), "vlad@test");

        Assert.Contains("тот же протокол", r.Refusal);
        Assert.Empty(c.Queue.Jobs);
    }

    [Fact]
    public async Task Import_PoolDiffersFromExistingDay_Refused()
    {
        SiblingDayPullService.ResetPendingForTests();
        var c = await SeedAsync(nameof(Import_PoolDiffersFromExistingDay_Refused));
        var id = ParsedSaturday(c, OtherSwims());

        var r = await c.Service().ImportAsync(c.DiscoveryId, Request(id, pool: "50m"), "vlad@test");

        Assert.Equal("у пятницы в базе 25 м, а выбрано 50 м. Мы добавляем субботу только с тем же бассейном, что у пятницы. "
            + "Если у пятницы бассейн записан неверно — сначала исправьте его через «Изменить»", r.Refusal);
    }

    [Fact]
    public async Task Import_NoEventName_ForSingleDay_Refused()
    {
        SiblingDayPullService.ResetPendingForTests();
        var c = await SeedAsync(nameof(Import_NoEventName_ForSingleDay_Refused));
        var id = ParsedSaturday(c, OtherSwims());

        var r = await c.Service().ImportAsync(c.DiscoveryId, Request(id, eventName: "  "), "vlad@test");

        Assert.Contains("название соревнования", r.Refusal);
    }

    [Fact]
    public async Task Import_ForeignPreviewEntry_Refused()
    {
        SiblingDayPullService.ResetPendingForTests();
        var c = await SeedAsync(nameof(Import_ForeignPreviewEntry_Refused));
        var id = ParsedSaturday(c, OtherSwims());

        var r = await c.Service().ImportAsync(c.DiscoveryId, Request(id) with { LogligId = 11589 }, "vlad@test");

        Assert.Contains("превью другого дня", r.Refusal);
    }

    [Fact]
    public async Task Import_ParallelCalls_SecondRefused_WhileFirstQueued()
    {
        SiblingDayPullService.ResetPendingForTests();
        var c = await SeedAsync(nameof(Import_ParallelCalls_SecondRefused_WhileFirstQueued));
        var a = ParsedSaturday(c, OtherSwims());
        var entryA = c.Previews.Object.GetEntry(a);
        var b = Guid.NewGuid();
        c.Previews.Setup(p => p.GetEntry(b)).Returns(entryA);

        var results = await Task.WhenAll(
            c.Service().ImportAsync(c.DiscoveryId, Request(a), "a@test"),
            c.Service(Open(nameof(Import_ParallelCalls_SecondRefused_WhileFirstQueued))).ImportAsync(c.DiscoveryId, Request(b), "b@test"));

        Assert.Single(c.Queue.Jobs);
        Assert.Single(results, r => r.Refusal == null);
        Assert.Contains("уже затягивается", Assert.Single(results, r => r.Refusal != null).Refusal);
    }

    // ── после импорта ────────────────────────────────────────────────────────

    private static SiblingDayTarget Target(Ctx c, int? eventId = null, string? name = "תחרות חלופית ל\"מילניום\"") =>
        new(c.DiscoveryId, 6611, 11590, c.FridayId, eventId, eventId == null ? name : null, "07/12/2024", "vlad@test");

    [Fact]
    public async Task Complete_SingleDay_JoinsWithCommonName()
    {
        var c = await SeedAsync(nameof(Complete_SingleDay_JoinsWithCommonName));
        var sat = new Competition { Name = "x", Date = "07/12/2024", PoolType = "25m" };
        c.Db.Competitions.Add(sat);
        await c.Db.SaveChangesAsync();
        c.Join.Setup(j => j.JoinAsync(It.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { c.FridayId, sat.Id })), true,
                It.IsAny<CancellationToken>(), "תחרות חלופית ל\"מילניום\""))
            .ReturnsAsync(new CompetitionDayJoinReport("תחרות חלופית ל\"מילניום\"", 6611,
                [new(c.FridayId, new DateOnly(2024, 12, 6), 1, 6611, 1), new(sat.Id, Sat, 2, null, 1)], [], null, true, 66));

        var done = await c.Service().CompleteAsync(Target(c), new ImportResult { Created = 1, CompetitionIds = [sat.Id] });

        Assert.True(done.Joined);
        Assert.Equal(66, done.EventId);
        Assert.Equal(sat.Id, done.CompetitionId);
        Assert.StartsWith("Готово. Теперь у соревнования", done.Message);
        c.Audit.Verify(a => a.LogAsync(SiblingDayPullService.AuditAction, "Competition", sat.Id.ToString(),
            It.Is<string>(s => s.Contains("vlad@test")), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Complete_JoinRefused_SaysHowToFinishByHand()
    {
        var c = await SeedAsync(nameof(Complete_JoinRefused_SaysHowToFinishByHand));
        var sat = new Competition { Name = "x", Date = "07/12/2024", PoolType = "25m" };
        c.Db.Competitions.Add(sat);
        await c.Db.SaveChangesAsync();
        c.Join.Setup(j => j.JoinAsync(It.IsAny<IReadOnlyList<int>>(), true, It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(CompetitionDayJoinReport.Refused("x", "у соревнований разный бассейн"));

        var done = await c.Service().CompleteAsync(Target(c), new ImportResult { Created = 1, CompetitionIds = [sat.Id] });

        Assert.False(done.Joined);
        Assert.Contains($"--join-days {c.FridayId},{sat.Id}", done.Message);
    }

    [Fact]
    public async Task Complete_ExistingEvent_NoJoin_ChecksDayInEvent()
    {
        var c = await SeedAsync(nameof(Complete_ExistingEvent_NoJoin_ChecksDayInEvent));
        var sat = new Competition { Name = "x", Date = "07/12/2024", PoolType = "25m", EventId = 65 };
        c.Db.Competitions.Add(sat);
        await c.Db.SaveChangesAsync();

        var done = await c.Service().CompleteAsync(Target(c, eventId: 65), new ImportResult { Created = 1, CompetitionIds = [sat.Id] });

        Assert.True(done.Joined);
        Assert.Equal(65, done.EventId);
        c.Join.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Complete_ImportWroteNothing_NoJoin()
    {
        var c = await SeedAsync(nameof(Complete_ImportWroteNothing_NoJoin));

        var done = await c.Service().CompleteAsync(Target(c), new ImportResult { Created = 0 });

        Assert.Null(done.CompetitionId);
        c.Join.VerifyNoOtherCalls();
    }

    // ── Сбои завершения (ревью PR #284) ──────────────────────────────────────

    private async Task<Competition> SaturdayAsync(Ctx c, int? eventId = null, int? dayNumber = null)
    {
        var sat = new Competition { Name = "x", Date = "07/12/2024", PoolType = "25m", EventId = eventId, DayNumber = dayNumber };
        c.Db.Competitions.Add(sat);
        await c.Db.SaveChangesAsync();
        return sat;
    }

    private static void AuditOnce(Ctx c, string contains) =>
        c.Audit.Verify(a => a.LogAsync(SiblingDayPullService.AuditAction, "Competition", It.IsAny<string?>(),
            It.Is<string>(s => s.Contains(contains)), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);

    [Fact]
    public async Task Complete_JoinThrowsAfterCommit_JoinedButRecalcFailed_DayStillReturned_Audited()
    {
        var c = await SeedAsync(nameof(Complete_JoinThrowsAfterCommit_JoinedButRecalcFailed_DayStillReturned_Audited));
        var sat = await SaturdayAsync(c);
        c.Join.Setup(j => j.JoinAsync(It.IsAny<IReadOnlyList<int>>(), true, It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .Returns(async () =>
            {
                // Сбор записан, пересчёт после commit упал.
                var ev = new CompetitionEvent { Name = "e" };
                c.Db.CompetitionEvents.Add(ev);
                await c.Db.SaveChangesAsync();
                foreach (var day in c.Db.Competitions) day.EventId = ev.Id;
                await c.Db.SaveChangesAsync();
                throw new TimeoutException("пересчёт: таймаут");
            });

        var done = await c.Service().CompleteAsync(Target(c), new ImportResult { Created = 1, CompetitionIds = [sat.Id] });

        Assert.True(done.Joined);
        Assert.Equal(sat.Id, done.CompetitionId); // фон по нему запланирует шаги loglig
        Assert.StartsWith("⚠ День 07.12 добавлен в соревнование, но пересчёт мест и очков не завершился", done.Message);
        AuditOnce(c, "завершение сорвалось");
    }

    [Fact]
    public async Task Complete_JoinThrowsBeforeWrite_NotJoined_HintHasName_Audited()
    {
        var c = await SeedAsync(nameof(Complete_JoinThrowsBeforeWrite_NotJoined_HintHasName_Audited));
        var sat = await SaturdayAsync(c);
        c.Join.Setup(j => j.JoinAsync(It.IsAny<IReadOnlyList<int>>(), true, It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("база недоступна"));

        var done = await c.Service().CompleteAsync(Target(c), new ImportResult { Created = 1, CompetitionIds = [sat.Id] });

        Assert.False(done.Joined);
        Assert.Equal(sat.Id, done.CompetitionId);
        Assert.Contains($"--join-days {c.FridayId},{sat.Id} --name \"תחרות חלופית ל\\\"מילניום\\\"\" --apply", done.Message);
        AuditOnce(c, "завершение сорвалось");
    }

    [Fact]
    public async Task Complete_ImportWroteNothing_Audited()
    {
        var c = await SeedAsync(nameof(Complete_ImportWroteNothing_Audited));

        var done = await c.Service().CompleteAsync(Target(c), new ImportResult { Created = 0, Message = "Дубль" });

        Assert.Contains("не записаны", done.Message);
        AuditOnce(c, "импорт ничего не записал");
    }

    [Fact]
    public async Task ImportFailed_Audited_AndReleasesTheDay()
    {
        SiblingDayPullService.ResetPendingForTests();
        var c = await SeedAsync(nameof(ImportFailed_Audited_AndReleasesTheDay));
        var id = ParsedSaturday(c, OtherSwims());
        Assert.Null((await c.Service().ImportAsync(c.DiscoveryId, Request(id), "a@test")).Refusal);

        await c.Service().ImportFailedAsync(Assert.Single(c.Queue.Jobs).Options!.SiblingDay!, "OutOfMemory");

        AuditOnce(c, "задача импорта упала");
        // Замок снят: следующий клик не получит «уже затягивается» (статус задачи в фейке — Queued).
        var again = ParsedSaturday(c, OtherSwims());
        Assert.Null((await c.Service().ImportAsync(c.DiscoveryId, Request(again), "a@test")).Refusal);
    }

    [Fact]
    public async Task Complete_ExistingDayGotEventWhileQueued_NotJoined_SaysWhy()
    {
        var c = await SeedAsync(nameof(Complete_ExistingDayGotEventWhileQueued_NotJoined_SaysWhy));
        var sat = await SaturdayAsync(c);
        var ev = new CompetitionEvent { Name = "e" };
        c.Db.CompetitionEvents.Add(ev);
        await c.Db.SaveChangesAsync();
        c.Db.Competitions.Single(x => x.Id == c.FridayId).EventId = ev.Id;
        await c.Db.SaveChangesAsync();

        var done = await c.Service().CompleteAsync(Target(c), new ImportResult { Created = 1, CompetitionIds = [sat.Id] });

        Assert.False(done.Joined);
        Assert.Contains("тем временем попал в событие", done.Message);
        c.Join.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Complete_IntoEvent_EarlierDay_RenumberedByDate()
    {
        var c = await SeedAsync(nameof(Complete_IntoEvent_EarlierDay_RenumberedByDate));
        var ev = new CompetitionEvent { Name = "e" };
        c.Db.CompetitionEvents.Add(ev);
        await c.Db.SaveChangesAsync();
        // Лежит суббота (день 1); затянули пятницу — импорт дал ей номер max+1 = 2.
        var fri = c.Db.Competitions.Single(x => x.Id == c.FridayId);
        fri.Date = "07/12/2024"; fri.EventId = ev.Id; fri.DayNumber = 1;
        var earlier = new Competition { Name = "e", Date = "05/12/2024", PoolType = "25m", EventId = ev.Id, DayNumber = 2 };
        c.Db.Competitions.Add(earlier);
        await c.Db.SaveChangesAsync();

        var target = new SiblingDayTarget(c.DiscoveryId, 6611, 11590, c.FridayId, ev.Id, null, "05/12/2024", "vlad@test");
        var done = await c.Service().CompleteAsync(target, new ImportResult { Created = 1, CompetitionIds = [earlier.Id] });

        Assert.True(done.Joined);
        c.Db.ChangeTracker.Clear();
        Assert.Equal(1, c.Db.Competitions.Single(x => x.Id == earlier.Id).DayNumber);
        Assert.Equal(2, c.Db.Competitions.Single(x => x.Id == c.FridayId).DayNumber);
    }

    [Fact]
    public async Task Preview_NoHeatsNoTimes_WarnsPlainly_DoesNotRefuse()
    {
        var c = await SeedAsync(nameof(Preview_NoHeatsNoTimes_WarnsPlainly_DoesNotRefuse));
        ParsedSaturday(c, Json(("כהן", "דנה", 0, 0, "freestyle"), ("לוי", "נועה", 0, 0, "freestyle")));

        var r = await c.Service().PreviewAsync(c.DiscoveryId, 11590);

        Assert.Null(r.Refusal);
        Assert.Equal(MissingDayTexts.OverlapNotChecked, r.Context!.OverlapNote);
    }
}
