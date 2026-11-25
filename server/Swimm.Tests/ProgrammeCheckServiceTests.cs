using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Swimm.Application.Dtos;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Р123 насквозь: строка входящих → программа (подделка сетки) → НАСТОЯЩИЙ сервис деления
/// (dry-run в проверке, запись в «Разделить»), аудит. Пути Postgres у деления (блокировка, повтор) —
/// в CompetitionDaySplitPgTests.
/// </summary>
public class ProgrammeCheckServiceTests
{
    private static readonly DateOnly Day1 = new(2025, 3, 28), Day2 = new(2025, 3, 29);

    private sealed class FakeDates(Func<int, DisciplineDates> dates) : IDisciplineDateSource
    {
        public int Calls;
        public FakeDates(Func<DisciplineDates> dates) : this(_ => dates()) { }
        public Task<DisciplineDates> FetchAsync(int logligId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(dates(Calls));
        }
    }

    private sealed class Recalc(bool fail = false) : ICompetitionRecalculationService
    {
        public Task<int> RecalculateCompetitionAsync(int competitionId, CancellationToken ct = default) =>
            fail ? throw new TimeoutException("пересчёт: таймаут") : Task.FromResult(0);
        public Task<int> RecalculateAllCombinedAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RecalculateAllParaPointsAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private static async Task<(SwimmDbContext Db, int DiscoveryId, int CompId)> SeedAsync(string name, int? logligId = 12160)
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var free = new Style { Name = "freestyle" };
        var fly = new Style { Name = "butterfly" };
        var swimmer = new Swimmer { FirstName = "נועה", LastName = "לוי", BirthYear = 2010 };
        var club = new Club { Name = "הפועל" };
        var comp = new Competition { Name = "אליפות עמק חפר", Date = "29/03/2025", PoolType = "25", OrgCompId = 6602 };
        var disc = new DiscoveredCompetition
        {
            OrgCompId = 6602, LogligId = logligId, Name = comp.Name,
            DateStart = new DateTime(2025, 3, 28), DateEnd = new DateTime(2025, 3, 29),
        };
        db.AddRange(free, fly, swimmer, club, comp, disc);
        await db.SaveChangesAsync();
        ResultRecord Row(Style s, string distance) => new()
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = s.Id,
            Distance = distance, Gender = "female", CompetitionDate = new DateTime(2025, 3, 29),
        };
        db.Results.AddRange(Row(free, "400"), Row(free, "400"), Row(fly, "200"));
        await db.SaveChangesAsync();
        return (db, disc.Id, comp.Id);
    }

    private static DisciplineDates Dates(DateOnly free400, DateOnly fly200) => new(
    [
        (new DisciplineDayKey("freestyle", "400", "female"), free400),
        (new DisciplineDayKey("butterfly", "200", "female"), fly200),
    ]);

    private static DisciplineDates TwoDays() => Dates(Day1, Day2);

    private static ProgrammeCheckService Service(SwimmDbContext db, IDisciplineDateSource dates,
        ICompetitionDiscoveryProvider? loglig = null, IAdminAuditService? audit = null, bool recalcFails = false) =>
        new(db, dates, new CompetitionDaySplitService(db, dates, new Recalc(recalcFails)),
            loglig ?? new Mock<ICompetitionDiscoveryProvider>(MockBehavior.Strict).Object,
            audit ?? Mock.Of<IAdminAuditService>(), NullLogger<ProgrammeCheckService>.Instance);

    private static void VerifyAudit(Mock<IAdminAuditService> audit, Func<string, bool> summary, Times times) =>
        audit.Verify(a => a.LogAsync("competition.split-days", "Competition", It.IsAny<string?>(),
            It.Is<string?>(s => s != null && summary(s)), It.IsAny<object?>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task Check_Collapsed_ShowsDryRunPlan_WritesNothing()
    {
        var (db, discId, compId) = await SeedAsync(nameof(Check_Collapsed_ShowsDryRunPlan_WritesNothing));
        await using var _ = db;

        var v = await Service(db, new FakeDates(TwoDays)).CheckAsync(discId);

        Assert.Equal(ProgrammeVerdictKind.Collapsed, v.Kind);
        Assert.Equal(compId, v.SplitCompetitionId);
        Assert.Equal(["день 1 28.03: 2 строки — новое соревнование", $"день 2 29.03: 1 строка — остаётся #{compId}"], v.Lines);
        Assert.Equal("2025-03-28=2;2025-03-29=1", v.PlanFingerprint);
        Assert.Equal(1, await db.Competitions.CountAsync());
    }

    [Fact]
    public async Task Split_Collapsed_AppliesSplit_Audited_RepeatRefused()
    {
        var (db, discId, compId) = await SeedAsync(nameof(Split_Collapsed_AppliesSplit_Audited_RepeatRefused));
        await using var _ = db;
        var audit = new Mock<IAdminAuditService>();
        var svc = Service(db, new FakeDates(TwoDays), audit: audit.Object);
        var fp = (await svc.CheckAsync(discId)).PlanFingerprint;

        var outcome = await svc.SplitAsync(discId, fp);
        var again = await svc.SplitAsync(discId, fp);

        Assert.Null(outcome.Refusal);
        Assert.True(outcome.Report!.Applied);
        var days = await db.Competitions.AsNoTracking().OrderBy(c => c.DayNumber).ToListAsync();
        Assert.Equal(["28/03/2025", "29/03/2025"], days.Select(d => d.Date));
        Assert.Equal(compId, days[1].Id);
        Assert.Equal(2, await db.Results.CountAsync(r => r.CompetitionId == days[0].Id));
        VerifyAudit(audit, s => s.Contains($"discovery #{discId}") && !s.Contains("ПЕРЕСЧЁТ"), Times.Once());

        // Повтор после успеха: дни уже в базе — делить нечего, второй записи нет.
        Assert.NotNull(again.Refusal);
        Assert.Equal(2, await db.Competitions.CountAsync());
        VerifyAudit(audit, _ => true, Times.Once());
    }

    [Fact]
    public async Task Split_StaleFingerprint_Refused_NothingWritten()
    {
        var (db, discId, _) = await SeedAsync(nameof(Split_StaleFingerprint_Refused_NothingWritten));
        await using var __ = db;

        var outcome = await Service(db, new FakeDates(TwoDays)).SplitAsync(discId, "2025-03-28=1;2025-03-29=2");

        Assert.Equal("план изменился — проверьте ещё раз", outcome.Refusal);
        Assert.Equal(1, await db.Competitions.CountAsync());
    }

    [Fact]
    public async Task Split_RecalcFailsAfterCommit_AuditWrittenAnyway()
    {
        var (db, discId, compId) = await SeedAsync(nameof(Split_RecalcFailsAfterCommit_AuditWrittenAnyway));
        await using var _ = db;
        var audit = new Mock<IAdminAuditService>();
        var svc = Service(db, new FakeDates(TwoDays), audit: audit.Object, recalcFails: true);
        var fp = (await svc.CheckAsync(discId)).PlanFingerprint;

        var outcome = await svc.SplitAsync(discId, fp);

        Assert.Null(outcome.Refusal);
        Assert.Contains("таймаут", outcome.RecalcError);
        Assert.Equal(2, outcome.Report!.Days.Count);
        Assert.Equal(2, await db.Competitions.CountAsync());
        VerifyAudit(audit, s => s.Contains("ПЕРЕСЧЁТ НЕ ЗАВЕРШЁН") && s.Contains($"#{compId}"), Times.Once());
    }

    [Fact]
    public async Task Split_ProgrammeDateOutsideFederation_NoButton_Refused()
    {
        var (db, discId, _) = await SeedAsync(nameof(Split_ProgrammeDateOutsideFederation_NoButton_Refused));
        await using var __ = db;
        // 400 вольным на loglig — 30.03 (опечатка), федерация 28–29.03.
        var svc = Service(db, new FakeDates(() => Dates(new DateOnly(2025, 3, 30), Day2)));

        var v = await svc.CheckAsync(discId);
        var outcome = await svc.SplitAsync(discId, null);

        Assert.Equal(ProgrammeVerdictKind.Collapsed, v.Kind);
        Assert.False(v.CanSplit);
        Assert.Contains("30.03 вне дат федерации", v.SplitBlocked);
        Assert.Contains("вне дат федерации", outcome.Refusal);
        Assert.Equal(1, await db.Competitions.CountAsync());
    }

    [Fact]
    public async Task Split_Ambiguous_Refused_NothingWritten()
    {
        var (db, discId, _) = await SeedAsync(nameof(Split_Ambiguous_Refused_NothingWritten));
        await using var __ = db;
        // 400 вольным — на обеих датах (отборы и финал): у строк нет однозначного дня.
        var ambiguous = new DisciplineDates(
        [
            (new DisciplineDayKey("freestyle", "400", "female"), Day1),
            (new DisciplineDayKey("freestyle", "400", "female"), Day2),
            (new DisciplineDayKey("butterfly", "200", "female"), Day2),
        ]);

        var svc = Service(db, new FakeDates(() => ambiguous));
        var v = await svc.CheckAsync(discId);
        var outcome = await svc.SplitAsync(discId, null);

        Assert.Equal(ProgrammeVerdictKind.Collapsed, v.Kind);
        Assert.False(v.CanSplit);
        Assert.NotNull(outcome.Refusal);
        Assert.Null(outcome.Report);
        Assert.Equal(1, await db.Competitions.CountAsync());
        Assert.Empty(await db.CompetitionEvents.ToListAsync());
    }

    [Fact]
    public async Task Check_DryRunLogligFails_HonestNoVerdict()
    {
        var (db, discId, _) = await SeedAsync(nameof(Check_DryRunLogligFails_HonestNoVerdict));
        await using var __ = db;
        // Первый запрос (программа) прошёл, второй (dry-run деления) упал.
        var dates = new FakeDates(call => call == 1 ? TwoDays() : throw new HttpRequestException("502"));

        var v = await Service(db, dates).CheckAsync(discId);

        Assert.Equal(ProgrammeVerdictKind.None, v.Kind);
        Assert.Contains("loglig не ответил", v.Headline);
    }

    [Fact]
    public async Task Check_NoLogligId_NoNetwork_NoVerdict()
    {
        var (db, discId, _) = await SeedAsync(nameof(Check_NoLogligId_NoNetwork_NoVerdict), logligId: null);
        await using var _ = db;
        var dates = new FakeDates(TwoDays);

        var v = await Service(db, dates).CheckAsync(discId);

        Assert.Equal(ProgrammeVerdictKind.None, v.Kind);
        Assert.Equal(0, dates.Calls);
    }

    [Fact]
    public async Task Check_LogligFails_HonestText()
    {
        var (db, discId, _) = await SeedAsync(nameof(Check_LogligFails_HonestText));
        await using var _ = db;

        var v = await Service(db, new FakeDates(() => throw new HttpRequestException("503"))).CheckAsync(discId);

        Assert.Equal(ProgrammeVerdictKind.None, v.Kind);
        Assert.Contains("loglig не ответил", v.Headline);
    }

    // ── Поиск соседей (б+в) ──

    private static DisciplineDates OnlySaturday() => Dates(Day2, Day2);

    private static Mock<ICompetitionDiscoveryProvider> Loglig(List<int> asked, Func<int, LogligCompetitionSummaryDto?> answer)
    {
        var loglig = new Mock<ICompetitionDiscoveryProvider>();
        loglig.Setup(p => p.FetchCompetitionSummaryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((int id, CancellationToken _) =>
            {
                asked.Add(id);
                return Task.FromResult(answer(id));
            });
        return loglig;
    }

    private static readonly HttpRequestException NotFound = new("404", null, System.Net.HttpStatusCode.NotFound);

    [Fact]
    public async Task Check_OnlyKnownDays_SearchesSixNeighbours_FindsMissingDay_SplitRefused()
    {
        var (db, discId, _) = await SeedAsync(nameof(Check_OnlyKnownDays_SearchesSixNeighbours_FindsMissingDay_SplitRefused));
        await using var _db = db;
        var asked = new List<int>();
        var loglig = Loglig(asked, id => id switch
        {
            12160 => new(id, "אליפות עמק חפר יום שבת 29.3.25 - 29/03/2025", [Day2]),
            12161 => new(id, "אליפות עמק חפר ביום שישי 28.3.25 - 28/03/2025", [Day1]),
            12162 => new(id, "אליפות עמק חפר יום שישי - 10/03/2025", []),
            _ => throw NotFound,
        });
        var svc = Service(db, new FakeDates(OnlySaturday), loglig.Object);

        var v = await svc.CheckAsync(discId);
        var split = await svc.SplitAsync(discId, null);

        Assert.Equal(ProgrammeVerdictKind.OnlyKnownDays, v.Kind);
        Assert.Equal([12160, 12157, 12158, 12159, 12161, 12162, 12163], asked.Take(7));
        Assert.Equal(12161, Assert.Single(v.Candidates!).LogligId);
        Assert.Equal(12162, Assert.Single(v.Similar!).LogligId);
        Assert.False(v.AcceptPrimary);
        // Вердикт не «склеены» — «Разделить» отказывает, ничего не пишет.
        Assert.NotNull(split.Refusal);
        Assert.Equal(1, await db.Competitions.CountAsync());
    }

    [Fact]
    public async Task Check_AllNeighbours404_IsNotFailure_AcceptPrimary()
    {
        var (db, discId, _) = await SeedAsync(nameof(Check_AllNeighbours404_IsNotFailure_AcceptPrimary));
        await using var _db = db;
        var loglig = Loglig([], id => id == 12160 ? new(id, "אליפות עמק חפר - 29/03/2025", [Day2]) : throw NotFound);

        var v = await Service(db, new FakeDates(OnlySaturday), loglig.Object).CheckAsync(discId);

        Assert.Equal(ProgrammeVerdictKind.OnlyKnownDays, v.Kind);
        Assert.True(v.AcceptPrimary);
        Assert.Empty(v.Candidates!);
    }

    [Fact]
    public async Task Check_NeighbourFails_CountedAsFailed_AcceptNotPrimary()
    {
        var (db, discId, _) = await SeedAsync(nameof(Check_NeighbourFails_CountedAsFailed_AcceptNotPrimary));
        await using var _db = db;
        var loglig = Loglig([], id => id switch
        {
            12160 => new(id, "אליפות עמק חפר - 29/03/2025", [Day2]),
            12159 => throw new HttpRequestException("503", null, System.Net.HttpStatusCode.ServiceUnavailable),
            _ => throw NotFound,
        });

        var v = await Service(db, new FakeDates(OnlySaturday), loglig.Object).CheckAsync(discId);

        Assert.False(v.AcceptPrimary);
        Assert.Contains("1 из 6 соседних номеров loglig не ответили", v.Hint);
    }
}
