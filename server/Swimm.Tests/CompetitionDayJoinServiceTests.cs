using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сбор отдельных соревнований в событие (docs/plans/multiday-split-plan.md, М4). Кейс — Эйлат-2025:
/// импорт разложил старт по датам (#1676 06/03 с compID 6601, #1677 08/03, #1678 11/03), но
/// события не завёл. Строки не двигаются, настройки дней не выравниваются — только называются.
/// </summary>
public class CompetitionDayJoinServiceTests
{
    private const string Eilat = "אליפות ישראל מים פתוחים אילת 2025";

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class RecalcSpy : ICompetitionRecalculationService
    {
        public readonly List<int> Calls = [];
        public Task<int> RecalculateCompetitionAsync(int competitionId, CancellationToken ct = default)
        {
            Calls.Add(competitionId);
            return Task.FromResult(0);
        }
        public Task<int> RecalculateAllCombinedAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RecalculateAllParaPointsAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed record World(SwimmDbContext Db, Competition Day1, Competition Day2, Competition Day3);

    private static async Task<World> SeedAsync(string name)
    {
        var db = CreateDb(name);
        var free = new Style { Name = "freestyle" };
        var swimmer = new Swimmer { FirstName = "יעל", LastName = "כהן", BirthYear = 2009 };
        var club = new Club { Name = "הפועל אילת" };
        // Порядок вставки нарочно не по дате: день события определяет дата, а не Id.
        var day3 = new Competition { Name = Eilat, Date = "11/03/2025", PoolType = "25m", ClubPointsDisabled = true, StandingKindOverride = "openwater" };
        var day1 = new Competition { Name = Eilat, Date = "06/03/2025", PoolType = "25m", OrgCompId = 6601, PointRuleClubsId = 8, StandingKindOverride = "openwater" };
        var day2 = new Competition { Name = Eilat, Date = "08/03/2025", PoolType = "25m", ClubPointsDisabled = true, StandingKindOverride = "openwater" };
        db.AddRange(free, swimmer, club, day3, day1, day2);
        await db.SaveChangesAsync();

        ResultRecord Row(Competition c, string distance, DateTime date) => new()
        {
            CompetitionId = c.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = free.Id,
            Distance = distance, Gender = "female", CompetitionDate = date,
        };
        db.Results.AddRange(
            Row(day1, "5000", new DateTime(2025, 3, 6)), Row(day1, "1600", new DateTime(2025, 3, 6)),
            Row(day2, "10000", new DateTime(2025, 3, 8)),
            Row(day3, "10000", new DateTime(2025, 3, 11)));
        await db.SaveChangesAsync();
        return new World(db, day1, day2, day3);
    }

    [Fact]
    public async Task Apply_MakesEvent_DaysByDate_CompIdOnEventAndDay_RowsUntouched()
    {
        var w = await SeedAsync(nameof(Apply_MakesEvent_DaysByDate_CompIdOnEventAndDay_RowsUntouched));
        await using var db = w.Db;
        var recalc = new RecalcSpy();
        var rowsBefore = await db.Results.AsNoTracking()
            .Select(r => new { r.Id, r.CompetitionId, r.CompetitionDate }).ToListAsync();

        var report = await new CompetitionDayJoinService(db, recalc)
            .JoinAsync([w.Day3.Id, w.Day1.Id, w.Day2.Id], apply: true);

        Assert.Null(report.Refusal);
        Assert.True(report.Applied);
        Assert.Equal(6601, report.OrgCompId);
        Assert.Equal([(w.Day1.Id, 1, 2), (w.Day2.Id, 2, 1), (w.Day3.Id, 3, 1)],
            report.Days.Select(d => (d.CompetitionId, d.DayNumber, d.Rows)));

        // Событие: compID поднят на него (Д2), даты — от первого до последнего дня.
        var ev = await db.CompetitionEvents.SingleAsync();
        Assert.Equal(ev.Id, report.EventId);
        Assert.Equal(6601, ev.OrgCompId);
        Assert.Equal(new DateOnly(2025, 3, 6), ev.StartDate);
        Assert.Equal(new DateOnly(2025, 3, 11), ev.EndDate);
        Assert.Equal(Eilat, ev.Name);

        // Дни: номер по дате, SubName = исходное имя (Р-сп-1), compID остался на своём дне (Р-сп-2).
        var days = await db.Competitions.AsNoTracking().OrderBy(c => c.DayNumber).ToListAsync();
        Assert.All(days, d => Assert.Equal((ev.Id, Eilat, Eilat), (d.EventId!.Value, d.Name, d.SubName)));
        Assert.Equal([w.Day1.Id, w.Day2.Id, w.Day3.Id], days.Select(d => d.Id));
        Assert.Equal([6601, null, null], days.Select(d => d.OrgCompId));

        // Настройки дней не выравниваются.
        Assert.Equal([8, null, null], days.Select(d => d.PointRuleClubsId));
        Assert.Equal([false, true, true], days.Select(d => d.ClubPointsDisabled));

        // Строки не двинулись.
        var rowsAfter = await db.Results.AsNoTracking()
            .Select(r => new { r.Id, r.CompetitionId, r.CompetitionDate }).ToListAsync();
        Assert.Equal(rowsBefore.OrderBy(r => r.Id), rowsAfter.OrderBy(r => r.Id));

        Assert.Equal([w.Day1.Id, w.Day2.Id, w.Day3.Id], recalc.Calls);
    }

    [Fact]
    public async Task DryRun_ReportsPlanAndDifferences_WritesNothing()
    {
        var w = await SeedAsync(nameof(DryRun_ReportsPlanAndDifferences_WritesNothing));
        await using var db = w.Db;
        var recalc = new RecalcSpy();

        var report = await new CompetitionDayJoinService(db, recalc)
            .JoinAsync([w.Day1.Id, w.Day2.Id, w.Day3.Id], apply: false);

        Assert.Null(report.Refusal);
        Assert.False(report.Applied);
        Assert.Equal(3, report.Days.Count);
        Assert.Contains(report.Differences, d => d.StartsWith("правило клубных очков")
            && d.Contains($"#{w.Day1.Id} 8") && d.Contains($"#{w.Day2.Id} —"));
        Assert.Contains(report.Differences, d => d.StartsWith("зачёт не ведётся"));
        Assert.DoesNotContain(report.Differences, d => d.StartsWith("вид зачёта")); // у всех openwater

        Assert.False(await db.CompetitionEvents.AnyAsync());
        Assert.All(await db.Competitions.AsNoTracking().ToListAsync(), c => Assert.Null(c.EventId));
        Assert.Empty(recalc.Calls);
    }

    [Fact]
    public async Task Refuses_AlreadyInEvent()
    {
        var w = await SeedAsync(nameof(Refuses_AlreadyInEvent));
        await using var db = w.Db;
        var ev = new CompetitionEvent { Name = Eilat };
        db.CompetitionEvents.Add(ev);
        await db.SaveChangesAsync();
        (await db.Competitions.SingleAsync(c => c.Id == w.Day2.Id)).EventId = ev.Id;
        await db.SaveChangesAsync();

        var report = await new CompetitionDayJoinService(db, new RecalcSpy())
            .JoinAsync([w.Day1.Id, w.Day2.Id], apply: true);

        Assert.Contains("уже день события", report.Refusal);
        Assert.Equal(1, await db.CompetitionEvents.CountAsync());
    }

    [Fact]
    public async Task Refuses_DifferentPool_TwoCompIds_SameDate_RowsOnOtherDate_SingleId()
    {
        await using var db = CreateDb(nameof(Refuses_DifferentPool_TwoCompIds_SameDate_RowsOnOtherDate_SingleId));
        var free = new Style { Name = "freestyle" };
        var swimmer = new Swimmer { FirstName = "א", LastName = "ב", BirthYear = 2000 };
        var club = new Club { Name = "ג" };
        var a = new Competition { Name = "X", Date = "01/05/2025", PoolType = "25m", OrgCompId = 1 };
        var b50 = new Competition { Name = "X", Date = "02/05/2025", PoolType = "50m" };
        var bOrg = new Competition { Name = "X", Date = "03/05/2025", PoolType = "25m", OrgCompId = 2 };
        var c = new Competition { Name = "X ", Date = "04/05/2025", PoolType = "25m" };
        var other = new Competition { Name = "Y", Date = "05/05/2025", PoolType = "25m" };
        db.AddRange(free, swimmer, club, a, b50, bOrg, c, other);
        await db.SaveChangesAsync();
        // Строка #c лежит датой соседнего дня — команда строки не двигает, значит отказ.
        db.Results.Add(new ResultRecord
        {
            CompetitionId = c.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = free.Id,
            Distance = "100", Gender = "female", CompetitionDate = new DateTime(2025, 5, 1),
        });
        await db.SaveChangesAsync();
        var service = new CompetitionDayJoinService(db, new RecalcSpy());

        Assert.Contains("минимум два", (await service.JoinAsync([a.Id], true)).Refusal);
        Assert.Contains("разный бассейн", (await service.JoinAsync([a.Id, b50.Id], true)).Refusal);
        Assert.Contains("разные compID", (await service.JoinAsync([a.Id, bOrg.Id], true)).Refusal);
        Assert.Contains("разные имена", (await service.JoinAsync([a.Id, other.Id], true)).Refusal);
        Assert.Contains("с другой датой", (await service.JoinAsync([a.Id, c.Id], true)).Refusal);
        Assert.Contains("не найдены", (await service.JoinAsync([a.Id, 99999], true)).Refusal);
        Assert.False(await db.CompetitionEvents.AnyAsync());
    }
}
