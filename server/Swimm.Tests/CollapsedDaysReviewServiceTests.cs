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
/// Обзор многодневок (docs/plans/multiday-split-plan.md, М4): схлопнутые (один день в базе,
/// несколько по сетке loglig) и «дни есть, события нет» (Эйлат-2025 #1676–1678: 06, 08, 11/03).
/// </summary>
public class CollapsedDaysReviewServiceTests
{
    private static readonly DateOnly Jan3 = new(2025, 1, 3), Jan4 = new(2025, 1, 4);

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>Сетка по loglig-id; неизвестный id — как упавший loglig.</summary>
    private sealed class GridByLoglig(Dictionary<int, DisciplineDates> grids) : IDisciplineDateSource
    {
        public readonly List<int> Asked = [];
        public Task<DisciplineDates> FetchAsync(int logligId, CancellationToken ct = default)
        {
            Asked.Add(logligId);
            return grids.TryGetValue(logligId, out var dates)
                ? Task.FromResult(dates)
                : throw new HttpRequestException("500 Internal Server Error");
        }
    }

    private sealed class NoRecalc : ICompetitionRecalculationService
    {
        public Task<int> RecalculateCompetitionAsync(int competitionId, CancellationToken ct = default) =>
            throw new InvalidOperationException("обзор не пишет");
        public Task<int> RecalculateAllCombinedAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RecalculateAllParaPointsAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private static DaysWithoutEventDay Day(int id, DateOnly date) => new(id, date, null, 0);

    [Fact]
    public void Group_EilatDaysWithGaps_OneGroup()
    {
        var groups = CollapsedDaysReviewService.GroupDaysWithoutEvent(
        [
            ("אליפות ישראל מים פתוחים אילת 2025", "25m", Day(1678, new(2025, 3, 11))),
            ("אליפות ישראל מים פתוחים אילת 2025", "25m", Day(1676, new(2025, 3, 6))),
            ("אליפות ישראל מים פתוחים אילת 2025", "25m", Day(1677, new(2025, 3, 8))),
        ]);

        var g = Assert.Single(groups);
        Assert.Equal([1676, 1677, 1678], g.Days.Select(d => d.CompetitionId));
    }

    [Fact]
    public void Group_LeagueRoundsWeeksApart_DifferentPool_DifferentName_NoGroup()
    {
        var groups = CollapsedDaysReviewService.GroupDaysWithoutEvent(
        [
            // Одноимённые туры — через три недели (как «איגוד השחייה בישראל» в базе).
            ("איגוד השחייה בישראל", "25m", Day(1, new(2025, 1, 1))),
            ("איגוד השחייה בישראל", "25m", Day(2, new(2025, 1, 22))),
            // Тот же день, но другой бассейн или другое имя — не один старт.
            ("גביע", "25m", Day(3, new(2025, 2, 1))),
            ("גביע", "50m", Day(4, new(2025, 2, 2))),
            ("גביע אחר", "25m", Day(5, new(2025, 2, 2))),
        ]);

        Assert.Empty(groups);
    }

    [Fact]
    public void Group_WindowCountsFromFirstDay()
    {
        // 0, 5, 9: девятый день дальше недели от первого — новая группа, и в ней он один.
        var groups = CollapsedDaysReviewService.GroupDaysWithoutEvent(
        [
            ("X", "25m", Day(1, new(2025, 5, 1))),
            ("X", "25m", Day(2, new(2025, 5, 6))),
            ("X", "25m", Day(3, new(2025, 5, 10))),
        ]);

        Assert.Equal([1, 2], Assert.Single(groups).Days.Select(d => d.CompetitionId));
    }

    [Fact]
    public async Task Review_FindsCollapsed_SkipsEventsAndGroups_ReportsFailures()
    {
        await using var db = CreateDb(nameof(Review_FindsCollapsed_SkipsEventsAndGroups_ReportsFailures));
        var free = new Style { Name = "freestyle" };
        var fly = new Style { Name = "butterfly" };
        var swimmer = new Swimmer { FirstName = "יעל", LastName = "פרידלנד", BirthYear = 1985 };
        var club = new Club { Name = "מכבי" };
        var ev = new CompetitionEvent { Name = "כבר אירוע", OrgCompId = 300 };

        var collapsed = new Competition { Name = "מאסטרס חורף", Date = "04/01/2025", PoolType = "25m", OrgCompId = 100 };
        var oneDay = new Competition { Name = "ליגה", Date = "10/02/2025", PoolType = "25m", OrgCompId = 200 };
        var inEvent = new Competition { Name = "כבר אירוע", Date = "01/03/2025", PoolType = "25m", OrgCompId = 300, Event = ev };
        var broken = new Competition { Name = "שבור", Date = "05/04/2025", PoolType = "25m", OrgCompId = 400 };
        var eilat1 = new Competition { Name = "אילת", Date = "06/03/2025", PoolType = "25m", OrgCompId = 500 };
        var eilat2 = new Competition { Name = "אילת", Date = "08/03/2025", PoolType = "25m" };
        db.AddRange(free, fly, swimmer, club, ev, collapsed, oneDay, inEvent, broken, eilat1, eilat2);
        db.DiscoveredCompetitions.AddRange(
            new DiscoveredCompetition { OrgCompId = 100, LogligId = 1, Name = collapsed.Name },
            new DiscoveredCompetition { OrgCompId = 200, LogligId = 2, Name = oneDay.Name },
            new DiscoveredCompetition { OrgCompId = 300, LogligId = 3, Name = inEvent.Name },
            new DiscoveredCompetition { OrgCompId = 400, LogligId = 4, Name = broken.Name },
            new DiscoveredCompetition { OrgCompId = 500, LogligId = 5, Name = eilat1.Name },
            new DiscoveredCompetition { OrgCompId = 999, LogligId = 9, Name = "לא יובא" });
        await db.SaveChangesAsync();

        ResultRecord Row(Competition c, Style s, string gender) => new()
        {
            CompetitionId = c.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = s.Id,
            Distance = "200", Gender = gender, CompetitionDate = new DateTime(2025, 1, 4),
        };
        db.Results.AddRange(
            Row(collapsed, free, "female"), Row(collapsed, fly, "female"), Row(collapsed, fly, "male"),
            Row(eilat1, free, "female"), Row(eilat2, free, "female"), Row(eilat2, free, "male"));
        await db.SaveChangesAsync();

        var grids = new GridByLoglig(new()
        {
            [1] = new([
                (new DisciplineDayKey("freestyle", "200", "female"), Jan3),
                (new DisciplineDayKey("butterfly", "200", "female"), Jan3),
                (new DisciplineDayKey("butterfly", "200", "male"), Jan4),
            ]),
            [2] = new([(new DisciplineDayKey("freestyle", "200", "female"), new DateOnly(2025, 2, 10))]),
        });
        var service = new CollapsedDaysReviewService(db, grids, new CompetitionDaySplitService(db, grids, new NoRecalc()));

        var review = await service.ReviewAsync(TimeSpan.Zero);

        // Сетку спрашиваем только у однодневных без события и вне группы «дни без события»;
        // у схлопнутого — второй раз из плана --split-days.
        Assert.Equal([1, 1, 2, 4], grids.Asked.Order());
        Assert.Equal(3, review.Checked);

        var c = Assert.Single(review.Collapsed);
        Assert.Equal((collapsed.Id, 100, 1), (c.Split.CompetitionId, c.OrgCompId, c.LogligId));
        Assert.Equal([Jan3, Jan4], c.SourceDays);
        Assert.Null(c.Split.Refusal);
        Assert.Equal([(Jan3, 2, false), (Jan4, 1, true)], c.Split.Days.Select(d => (d.Date, d.Rows, d.Existing)));
        Assert.False(c.Split.Applied);

        var g = Assert.Single(review.DaysWithoutEvent);
        Assert.Equal("אילת", g.Name);
        Assert.Equal([(eilat1.Id, 1, (int?)500), (eilat2.Id, 2, null)], g.Days.Select(d => (d.CompetitionId, d.Rows, d.OrgCompId)));

        Assert.Contains("loglig 4", Assert.Single(review.Failed));

        // Только чтение: ни события, ни переноса строк.
        Assert.Equal(1, await db.CompetitionEvents.CountAsync());
        Assert.Equal(3, await db.Results.CountAsync(r => r.CompetitionId == collapsed.Id));
    }

    [Fact]
    public async Task Split_AllRowsOnOneDayOfMultiDayGrid_SaysOtherDaysLieElsewhere()
    {
        await using var db = CreateDb(nameof(Split_AllRowsOnOneDayOfMultiDayGrid_SaysOtherDaysLieElsewhere));
        var free = new Style { Name = "freestyle" };
        var swimmer = new Swimmer { FirstName = "א", LastName = "ב", BirthYear = 2000 };
        var club = new Club { Name = "ג" };
        var comp = new Competition { Name = "אילת", Date = "06/03/2025", PoolType = "25m", OrgCompId = 500 };
        db.AddRange(free, swimmer, club, comp);
        await db.SaveChangesAsync();
        db.Results.Add(new ResultRecord
        {
            CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = club.Id, StyleId = free.Id,
            Distance = "200", Gender = "female", CompetitionDate = new DateTime(2025, 3, 6),
        });
        await db.SaveChangesAsync();
        var grids = new GridByLoglig(new()
        {
            [5] = new([
                (new DisciplineDayKey("freestyle", "200", "female"), new DateOnly(2025, 3, 6)),
                (new DisciplineDayKey("freestyle", "5000", "female"), new DateOnly(2025, 3, 8)),
            ]),
        });

        var report = await new CompetitionDaySplitService(db, grids, new NoRecalc()).SplitAsync(comp.Id, 5, apply: false);

        Assert.Contains("остальные дни лежат не здесь", report.Refusal);
    }
}
