using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Деление схлопнутой многодневки по дням без переимпорта (docs/plans/multiday-split-plan.md, М2).
/// Кейс — мастерс-зима 2025 (#1645): все строки на 04/01, а 200 баттерфляй женщины и 400
/// вольным плыли 03/01. Строки ПЕРЕНОСЯТСЯ — Id те же, медиа едут следом.
/// </summary>
public class CompetitionDaySplitServiceTests
{
    private static readonly DateOnly Day1 = new(2025, 1, 3), Day2 = new(2025, 1, 4);

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class FakeDates(DisciplineDates dates) : IDisciplineDateSource
    {
        public int? AskedFor;
        public Task<DisciplineDates> FetchAsync(int logligId, CancellationToken ct = default)
        {
            AskedFor = logligId;
            return Task.FromResult(dates);
        }
    }

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

    private static DisciplineDates TwoDays() => new(
    [
        (new DisciplineDayKey("freestyle", "400", "female"), Day1),
        (new DisciplineDayKey("butterfly", "200", "female"), Day1),
        (new DisciplineDayKey("butterfly", "200", "male"), Day2),
    ]);

    private sealed record World(SwimmDbContext Db, Competition Comp, long Free400, long FlyWomen, long FlyMen, int MediaId);

    private static async Task<World> SeedAsync(string name)
    {
        var db = CreateDb(name);
        var free = new Style { Name = "freestyle" };
        var fly = new Style { Name = "butterfly" };
        var swimmer = new Swimmer { FirstName = "יעל", LastName = "פרידלנד", BirthYear = 1985 };
        var club = new Club { Name = "מכבי עולם המים" };
        var comp = new Competition
        {
            Name = "אליפות ישראל arena למאסטרס חורף 2025", Date = "04/01/2025", PoolType = "25",
            OrgCompId = 6595, IsMasters = true, IsChampionship = true, HasSplits = true, PointRuleClubsId = 2,
            HasOfficialClubStanding = true,
        };
        db.AddRange(free, fly, swimmer, club, comp);
        db.CategoryCompetitions.Add(new CategoryCompetition { CategoryId = 2, Competition = comp, DisplayOrder = 3 });
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition { OrgCompId = 6595, LogligId = 11586, Name = comp.Name });
        await db.SaveChangesAsync();

        ResultRecord Row(Style s, string distance, string gender) => new()
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = s.Id,
            Distance = distance, Gender = gender, CompetitionDate = new DateTime(2025, 1, 4),
        };
        var free400 = Row(free, "400", "female");
        var flyWomen = Row(fly, "200", "female");
        var flyMen = Row(fly, "200", "male");
        db.Results.AddRange(free400, flyWomen, flyMen);
        await db.SaveChangesAsync();

        var media = new UserMedia
        {
            UserId = 1, SwimmerId = swimmer.Id, ResultId = flyWomen.Id, CompetitionId = comp.Id,
            Level = "result", MediaType = "video", SourceType = "youtube", Url = "https://youtu.be/x",
        };
        db.UserMedia.Add(media);
        await db.SaveChangesAsync();
        return new World(db, comp, free400.Id, flyWomen.Id, flyMen.Id, media.Id);
    }

    [Fact]
    public async Task Apply_MovesFirstDayRows_KeepsIdsAndCompIdOnTheExistingDay()
    {
        var w = await SeedAsync(nameof(Apply_MovesFirstDayRows_KeepsIdsAndCompIdOnTheExistingDay));
        await using var db = w.Db;
        var dates = new FakeDates(TwoDays());
        var recalc = new RecalcSpy();

        var report = await new CompetitionDaySplitService(db, dates, recalc).SplitAsync(w.Comp.Id, null, apply: true);

        Assert.Null(report.Refusal);
        Assert.True(report.Applied);
        Assert.Equal(11586, dates.AskedFor); // LogligId — из discovery по compID

        // Событие несёт compID (Д2), даты — по дням.
        var ev = await db.CompetitionEvents.SingleAsync();
        Assert.Equal((6595, Day1, Day2, w.Comp.Name), (ev.OrgCompId, ev.StartDate, ev.EndDate, ev.Name));

        // Делимое соревнование — день 2 со своей датой и compID; новый — день 1, копия настроек.
        var days = await db.Competitions.AsNoTracking().OrderBy(c => c.DayNumber).ToListAsync();
        Assert.Equal(2, days.Count);
        var (first, second) = (days[0], days[1]);
        Assert.Equal(w.Comp.Id, second.Id);
        Assert.Equal(("04/01/2025", 2, 6595, ev.Id), (second.Date, second.DayNumber, second.OrgCompId, second.EventId));
        Assert.Equal(("03/01/2025", 1, (int?)null, ev.Id), (first.Date, first.DayNumber, first.OrgCompId, first.EventId));
        Assert.Equal(w.Comp.Name, first.Name);
        Assert.Equal(w.Comp.Name, first.SubName);
        Assert.True(first.IsMasters && first.IsChampionship && first.HasSplits && first.HasOfficialClubStanding == true);
        Assert.Equal(2, first.PointRuleClubsId);

        // Строки — те же Id, день и дата — по дисциплине.
        var rows = await db.Results.AsNoTracking().ToDictionaryAsync(r => r.Id);
        Assert.Equal((first.Id, new DateTime(2025, 1, 3)), (rows[w.Free400].CompetitionId, rows[w.Free400].CompetitionDate));
        Assert.Equal((first.Id, new DateTime(2025, 1, 3)), (rows[w.FlyWomen].CompetitionId, rows[w.FlyWomen].CompetitionDate));
        Assert.Equal((second.Id, new DateTime(2025, 1, 4)), (rows[w.FlyMen].CompetitionId, rows[w.FlyMen].CompetitionDate));

        // Медиа на строке — следом за строкой; категория — у нового дня тоже.
        Assert.Equal(first.Id, (await db.UserMedia.SingleAsync(m => m.Id == w.MediaId)).CompetitionId);
        Assert.True(await db.CategoryCompetitions.AnyAsync(cc => cc.CategoryId == 2 && cc.CompetitionId == first.Id));

        Assert.Equal([first.Id, second.Id], recalc.Calls);
        Assert.Equal([(Day1, 2, 1), (Day2, 1, 0)], report.Days.Select(d => (d.Date, d.Rows, d.Media)));
    }

    [Fact]
    public async Task DryRun_ReportsPlan_WritesNothing()
    {
        var w = await SeedAsync(nameof(DryRun_ReportsPlan_WritesNothing));
        await using var db = w.Db;
        var recalc = new RecalcSpy();

        var report = await new CompetitionDaySplitService(db, new FakeDates(TwoDays()), recalc)
            .SplitAsync(w.Comp.Id, null, apply: false);

        Assert.Null(report.Refusal);
        Assert.False(report.Applied);
        Assert.Equal([(Day1, 1, (int?)null, false, 2), (Day2, 2, w.Comp.Id, true, 1)],
            report.Days.Select(d => (d.Date, d.DayNumber, d.CompetitionId, d.Existing, d.Rows)));
        Assert.Equal(1, await db.Competitions.CountAsync());
        Assert.Empty(await db.CompetitionEvents.ToListAsync());
        Assert.All(await db.Results.ToListAsync(), r => Assert.Equal(w.Comp.Id, r.CompetitionId));
        Assert.Empty(recalc.Calls);
    }

    /// <summary>Всё или ничего: строка без дня, чужая дата, уже событие, однодневный источник.</summary>
    [Fact]
    public async Task Refusals_WriteNothing()
    {
        var w = await SeedAsync(nameof(Refusals_WriteNothing));
        await using var db = w.Db;
        var svc = (DisciplineDates d) => new CompetitionDaySplitService(db, new FakeDates(d), new RecalcSpy());

        // 200 баттерфляй мужчины у источника нет — строка без дня.
        var missing = new DisciplineDates(
        [
            (new DisciplineDayKey("freestyle", "400", "female"), Day1),
            (new DisciplineDayKey("butterfly", "200", "female"), Day2),
        ]);
        var r1 = await svc(missing).SplitAsync(w.Comp.Id, null, apply: true);
        Assert.Contains("нет однозначного дня", r1.Refusal);
        Assert.Equal(1, r1.Unresolved["butterfly 200 male"]);

        // Дни источника — 03 и 05/01, а соревнование 04/01: какой день оставить, неясно.
        var otherDays = new DisciplineDates(
        [
            (new DisciplineDayKey("freestyle", "400", "female"), Day1),
            (new DisciplineDayKey("butterfly", "200", "female"), Day1),
            (new DisciplineDayKey("butterfly", "200", "male"), new DateOnly(2025, 1, 5)),
        ]);
        Assert.Contains("не совпадает", (await svc(otherDays).SplitAsync(w.Comp.Id, null, apply: true)).Refusal);

        // Один день у источника — делить нечего.
        var oneDay = new DisciplineDates(
        [
            (new DisciplineDayKey("freestyle", "400", "female"), Day2),
            (new DisciplineDayKey("butterfly", "200", "female"), Day2),
            (new DisciplineDayKey("butterfly", "200", "male"), Day2),
        ]);
        Assert.Contains("один день", (await svc(oneDay).SplitAsync(w.Comp.Id, null, apply: true)).Refusal);

        Assert.Equal(1, await db.Competitions.CountAsync());
        Assert.Empty(await db.CompetitionEvents.ToListAsync());

        // Уже день события — повторный прогон ничего не делает.
        var comp = await db.Competitions.SingleAsync();
        comp.EventId = 99;
        await db.SaveChangesAsync();
        Assert.Contains("уже день события", (await svc(TwoDays()).SplitAsync(w.Comp.Id, null, apply: true)).Refusal);
    }
}
