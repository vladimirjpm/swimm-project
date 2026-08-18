using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Флаг «есть ли у соревнования официальный клубный зачёт» и подстановка правила под его шкалу.
///
/// Главное различение: «зачёта нет» (проверили — не публикуют) и «не проверили» (сайт лежал,
/// нет loglig-id). Второе НЕ должно записываться как false — иначе соревнование навсегда
/// останется «сверять не с чем».
/// </summary>
public class OfficialClubStandingServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>Фейк loglig: ответ на соревнование задаётся тестом (null = сайт недоступен).</summary>
    private sealed class FakeLoglig(LogligCompetitionStanding? standing) : ILogligClient
    {
        public int Calls { get; private set; }

        public Task<LogligPlayerCard?> GetPlayerCardAsync(int logligId, CancellationToken ct = default)
            => Task.FromResult<LogligPlayerCard?>(null);

        public string BuildPublicProfileUrl(int logligId) => $"https://loglig.com:2053/Players/Details/{logligId}";

        public Task<LogligCompetitionStanding?> GetCompetitionStandingAsync(
            int logligId, int scaleSampleEvents = 12, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(standing);
        }
    }

    private static OfficialClubStandingService Service(SwimmDbContext db, ILogligClient loglig) =>
        new(db, loglig, NullLogger<OfficialClubStandingService>.Instance);

    private static PointRuleClubs Rule(int id, string version, int maxPlace, params int[] points) => new()
    {
        Id = id, Version = version, Scope = "all", MaxScoringPlace = maxPlace,
        EffectiveFrom = new DateOnly(2025, 1, 1),
        Entries = points.Select((p, i) => new PointRuleClubsEntry { Place = i + 1, Points = p }).ToList()
    };

    private static readonly int[] BogrimPoints =
        [25, 22, 20, 18, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1];

    private static Dictionary<int, int> ScaleOf(params int[] points) =>
        points.Select((p, i) => (Place: i + 1, Points: p)).ToDictionary(x => x.Place, x => x.Points);

    private static Competition Comp(int id, int? orgCompId, int? eventId = null) => new()
    {
        Id = id, Name = $"Meet {id}", Date = "10/01/2026", PoolType = "50m",
        OrgCompId = orgCompId, EventId = eventId
    };

    // ── ProbeAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Probe_NoStanding_SaysNothingToCompareWith()
    {
        await using var db = CreateDb(nameof(Probe_NoStanding_SaysNothingToCompareWith));
        var probe = await Service(db, new FakeLoglig(new LogligCompetitionStanding(false, new Dictionary<int, int>())))
            .ProbeAsync(14729);

        Assert.False(probe.HasStanding);
        Assert.Null(probe.MatchedRuleId);
        Assert.Contains("сверять не с чем", probe.Message);
    }

    [Fact]
    public async Task Probe_StandingWithKnownScale_MatchesRule()
    {
        await using var db = CreateDb(nameof(Probe_StandingWithKnownScale_MatchesRule));
        db.Add(Rule(1, "30pt.24pl.2025.01", 24, 30, 28, 26, 24, 23, 22));
        db.Add(Rule(4, "25pt.20pl.2026.01", 20, BogrimPoints));
        await db.SaveChangesAsync();

        var probe = await Service(db, new FakeLoglig(new LogligCompetitionStanding(true, ScaleOf(BogrimPoints))))
            .ProbeAsync(14561);

        Assert.True(probe.HasStanding);
        Assert.Equal(4, probe.MatchedRuleId);
        Assert.Equal("25pt.20pl.2026.01", probe.MatchedRuleVersion);
        Assert.Contains("совпала", probe.Message);
    }

    [Fact]
    public async Task Probe_StandingWithUnknownScale_TellsToCreateRule()
    {
        // Случай «Хапоэля» до того, как шкалу завели: молча уйти на автоподбор нельзя —
        // соревнование получит чужие очки, и это всплывёт только на ручной сверке.
        await using var db = CreateDb(nameof(Probe_StandingWithUnknownScale_TellsToCreateRule));
        db.Add(Rule(4, "25pt.20pl.2026.01", 20, BogrimPoints));
        await db.SaveChangesAsync();

        var probe = await Service(db, new FakeLoglig(
                new LogligCompetitionStanding(true, ScaleOf(30, 26, 23, 20, 18, 16))))
            .ProbeAsync(14729);

        Assert.True(probe.HasStanding);
        Assert.Null(probe.MatchedRuleId);
        Assert.Contains("не совпала ни с одним правилом", probe.Message);
    }

    [Fact]
    public async Task Probe_SiteUnavailable_IsNotSameAsNoStanding()
    {
        await using var db = CreateDb(nameof(Probe_SiteUnavailable_IsNotSameAsNoStanding));
        var probe = await Service(db, new FakeLoglig(null)).ProbeAsync(14729);

        Assert.False(probe.HasStanding);
        Assert.Contains("недоступен", probe.Message);
    }

    // ── ProbeAndStampAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task Stamp_MarksEveryDayOfEvent()
    {
        await using var db = CreateDb(nameof(Stamp_MarksEveryDayOfEvent));
        db.Add(new CompetitionEvent { Id = 7, Name = "Champs" });
        // OrgCompId проставлен только «голове» события — дни ищутся через EventId.
        db.Add(Comp(10, orgCompId: 555, eventId: 7));
        db.Add(Comp(11, orgCompId: null, eventId: 7));
        db.Add(new DiscoveredCompetition { Id = 1, OrgCompId = 555, LogligId = 14561, Name = "Champs" });
        await db.SaveChangesAsync();

        var probe = await Service(db, new FakeLoglig(new LogligCompetitionStanding(true, ScaleOf(BogrimPoints))))
            .ProbeAndStampAsync(555);

        Assert.NotNull(probe);
        Assert.All(await db.Competitions.ToListAsync(), c => Assert.True(c.HasOfficialClubStanding));
    }

    [Fact]
    public async Task Stamp_NoStanding_WritesFalse_NotNull()
    {
        // «Проверено, зачёта нет» — значимый факт: он снимает вопрос «а почему не сверяли».
        await using var db = CreateDb(nameof(Stamp_NoStanding_WritesFalse_NotNull));
        db.Add(Comp(10, orgCompId: 555));
        db.Add(new DiscoveredCompetition { Id = 1, OrgCompId = 555, LogligId = 13692, Name = "Лига" });
        await db.SaveChangesAsync();

        await Service(db, new FakeLoglig(new LogligCompetitionStanding(false, new Dictionary<int, int>())))
            .ProbeAndStampAsync(555);

        Assert.False((await db.Competitions.FindAsync(10))!.HasOfficialClubStanding);
    }

    [Fact]
    public async Task Stamp_SiteUnavailable_LeavesFlagUnknown()
    {
        await using var db = CreateDb(nameof(Stamp_SiteUnavailable_LeavesFlagUnknown));
        db.Add(Comp(10, orgCompId: 555));
        db.Add(new DiscoveredCompetition { Id = 1, OrgCompId = 555, LogligId = 13692, Name = "Лига" });
        await db.SaveChangesAsync();

        var probe = await Service(db, new FakeLoglig(null)).ProbeAndStampAsync(555);

        Assert.Null(probe);
        Assert.Null((await db.Competitions.FindAsync(10))!.HasOfficialClubStanding);
    }

    [Fact]
    public async Task Stamp_WithoutLogligId_DoesNotEvenAsk()
    {
        await using var db = CreateDb(nameof(Stamp_WithoutLogligId_DoesNotEvenAsk));
        db.Add(Comp(10, orgCompId: 555));
        await db.SaveChangesAsync();
        var loglig = new FakeLoglig(new LogligCompetitionStanding(true, ScaleOf(BogrimPoints)));

        var probe = await Service(db, loglig).ProbeAndStampAsync(555);

        Assert.Null(probe);
        Assert.Equal(0, loglig.Calls);
        Assert.Null((await db.Competitions.FindAsync(10))!.HasOfficialClubStanding);
    }

    // ── Backfill ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Backfill_CountsCheckedAndUnknown_AndSkipsAlreadyMarked()
    {
        await using var db = CreateDb(nameof(Backfill_CountsCheckedAndUnknown_AndSkipsAlreadyMarked));
        db.Add(Comp(10, orgCompId: 555));
        db.Add(Comp(20, orgCompId: 666));                       // без loglig-id → «не проверено»
        var marked = Comp(30, orgCompId: 777);
        marked.HasOfficialClubStanding = false;                 // уже проверяли — без force не трогаем
        db.Add(marked);
        db.Add(new DiscoveredCompetition { Id = 1, OrgCompId = 555, LogligId = 14561, Name = "Champs" });
        db.Add(new DiscoveredCompetition { Id = 2, OrgCompId = 777, LogligId = 13692, Name = "Лига" });
        await db.SaveChangesAsync();

        var loglig = new FakeLoglig(new LogligCompetitionStanding(true, ScaleOf(BogrimPoints)));
        var report = await Service(db, loglig).BackfillAsync();

        Assert.Equal(1, report.Checked);
        Assert.Equal(1, report.WithStanding);
        Assert.Equal(1, report.Unknown);
        Assert.True((await db.Competitions.FindAsync(10))!.HasOfficialClubStanding);
        Assert.False((await db.Competitions.FindAsync(30))!.HasOfficialClubStanding); // не перезаписан
    }

    [Fact]
    public async Task Backfill_Force_RechecksAlreadyMarked()
    {
        // Зачёт публикуют не сразу — «нет» вчера не значит «нет» сегодня.
        await using var db = CreateDb(nameof(Backfill_Force_RechecksAlreadyMarked));
        var marked = Comp(30, orgCompId: 777);
        marked.HasOfficialClubStanding = false;
        db.Add(marked);
        db.Add(new DiscoveredCompetition { Id = 2, OrgCompId = 777, LogligId = 13692, Name = "Лига" });
        await db.SaveChangesAsync();

        var report = await Service(db, new FakeLoglig(new LogligCompetitionStanding(true, ScaleOf(BogrimPoints))))
            .BackfillAsync(force: true);

        Assert.Equal(1, report.WithStanding);
        Assert.True((await db.Competitions.FindAsync(30))!.HasOfficialClubStanding);
    }
}
