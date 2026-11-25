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
/// (dry-run в проверке, запись в «Разделить»). Пути Postgres у деления — в CompetitionDaySplitPgTests.
/// </summary>
public class ProgrammeCheckServiceTests
{
    private static readonly DateOnly Day1 = new(2025, 3, 28), Day2 = new(2025, 3, 29);

    private sealed class FakeDates(Func<DisciplineDates> dates) : IDisciplineDateSource
    {
        public int Calls;
        public Task<DisciplineDates> FetchAsync(int logligId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(dates());
        }
    }

    private sealed class NoRecalc : ICompetitionRecalculationService
    {
        public Task<int> RecalculateCompetitionAsync(int competitionId, CancellationToken ct = default) => Task.FromResult(0);
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

    private static DisciplineDates TwoDays() => new(
    [
        (new DisciplineDayKey("freestyle", "400", "female"), Day1),
        (new DisciplineDayKey("butterfly", "200", "female"), Day2),
    ]);

    private static ProgrammeCheckService Service(SwimmDbContext db, IDisciplineDateSource dates, ICompetitionDiscoveryProvider? loglig = null) =>
        new(db, dates, new CompetitionDaySplitService(db, dates, new NoRecalc()),
            loglig ?? new Mock<ICompetitionDiscoveryProvider>(MockBehavior.Strict).Object, NullLogger<ProgrammeCheckService>.Instance);

    [Fact]
    public async Task Check_Collapsed_ShowsDryRunPlan_WritesNothing()
    {
        var (db, discId, compId) = await SeedAsync(nameof(Check_Collapsed_ShowsDryRunPlan_WritesNothing));
        await using var _ = db;

        var v = await Service(db, new FakeDates(TwoDays)).CheckAsync(discId);

        Assert.Equal(ProgrammeVerdictKind.Collapsed, v.Kind);
        Assert.Equal(compId, v.SplitCompetitionId);
        Assert.Equal(["день 1 28.03: 2 строки — новое соревнование", $"день 2 29.03: 1 строка — остаётся #{compId}"], v.Lines);
        Assert.Equal(1, await db.Competitions.CountAsync());
    }

    [Fact]
    public async Task Split_Collapsed_AppliesSplit_NewDayAppears()
    {
        var (db, discId, compId) = await SeedAsync(nameof(Split_Collapsed_AppliesSplit_NewDayAppears));
        await using var _ = db;

        var outcome = await Service(db, new FakeDates(TwoDays)).SplitAsync(discId);

        Assert.Null(outcome.Refusal);
        Assert.True(outcome.Report!.Applied);
        var days = await db.Competitions.AsNoTracking().OrderBy(c => c.DayNumber).ToListAsync();
        Assert.Equal(["28/03/2025", "29/03/2025"], days.Select(d => d.Date));
        Assert.Equal(compId, days[1].Id);
        Assert.Equal(2, await db.Results.CountAsync(r => r.CompetitionId == days[0].Id));
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
        var outcome = await svc.SplitAsync(discId);

        Assert.Equal(ProgrammeVerdictKind.Collapsed, v.Kind);
        Assert.False(v.CanSplit);
        Assert.NotNull(outcome.Refusal);
        Assert.Null(outcome.Report);
        Assert.Equal(1, await db.Competitions.CountAsync());
        Assert.Empty(await db.CompetitionEvents.ToListAsync());
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

    [Fact]
    public async Task Check_OnlyKnownDays_SearchesSixNeighbours_FindsSaturday()
    {
        var (db, discId, _) = await SeedAsync(nameof(Check_OnlyKnownDays_SearchesSixNeighbours_FindsSaturday));
        await using var _db = db;
        // База и программа — только 29.03 (как пятница 6611); федерация 28–29.03 → недостающий 28.03.
        var oneDay = new DisciplineDates(
        [
            (new DisciplineDayKey("freestyle", "400", "female"), Day2),
            (new DisciplineDayKey("butterfly", "200", "female"), Day2),
        ]);
        var asked = new List<int>();
        var loglig = new Mock<ICompetitionDiscoveryProvider>();
        loglig.Setup(p => p.FetchCompetitionSummaryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((int id, CancellationToken _) =>
            {
                asked.Add(id);
                return id switch
                {
                    12160 => Task.FromResult<LogligCompetitionSummaryDto?>(new(id, "אליפות עמק חפר יום שבת 29.3.25 - 29/03/2025", [Day2])),
                    12161 => Task.FromResult<LogligCompetitionSummaryDto?>(new(id, "אליפות עמק חפר יום שישי 28.3.25 - 28/03/2025", [Day1])),
                    12162 => Task.FromResult<LogligCompetitionSummaryDto?>(new(id, "אליפות עמק חפר יום שישי - 10/03/2025", [])),
                    _ => throw new HttpRequestException("404", null, System.Net.HttpStatusCode.NotFound),
                };
            });

        var v = await Service(db, new FakeDates(() => oneDay), loglig.Object).CheckAsync(discId);

        Assert.Equal(ProgrammeVerdictKind.OnlyKnownDays, v.Kind);
        Assert.Equal([12160, 12157, 12158, 12159, 12161, 12162, 12163], asked);
        Assert.Equal(12161, Assert.Single(v.Candidates!).LogligId);
        Assert.Equal(12162, Assert.Single(v.Similar!).LogligId);
        Assert.False(v.AcceptPrimary);
    }
}
