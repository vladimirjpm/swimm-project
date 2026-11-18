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
/// Сверка кандидатов на смену клуба с loglig (Р98): подтверждённая смена записывается с журналом
/// «loglig», прочие ждут со своим вердиктом, ответ loglig кэшируется на сутки, dry-run ничего не
/// пишет, закреплённого не трогаем. Сеть — подставной <see cref="ILogligClient"/>.
/// </summary>
public class SwimmerClubLogligCheckServiceTests
{
    private const int Dolphin = 438, Raanana = 449, Shoham = 446;

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class FakeLoglig(Dictionary<int, string?> clubs) : ILogligClient
    {
        public List<int> Calls { get; } = [];

        public Task<LogligPlayerCard?> GetPlayerCardAsync(int logligId, int? seasonId = null, CancellationToken ct = default)
        {
            Calls.Add(logligId);
            return Task.FromResult(clubs.TryGetValue(logligId, out var club)
                ? new LogligPlayerCard("X", 2012, "F", club, [])
                : null);
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

    private static SwimmerClubLogligCheckService Service(SwimmDbContext db, FakeLoglig loglig) =>
        new(db, loglig, NullLogger<SwimmerClubLogligCheckService>.Instance, TimeSpan.Zero);

    /// <summary>
    /// Пятеро кандидатов «Дельфин → Раанана»: loglig у 1 — Раанана, у 2 — Дельфин, у 3 — архив,
    /// у 4 — Шохам, у 5 карточка не открывается; 6 — без loglig-id, 7 — loglig-id только предложен.
    /// </summary>
    private static async Task SeedAsync(SwimmDbContext db, bool lockFirst = false)
    {
        db.Clubs.AddRange(
            new Club { Id = Dolphin, Name = "הפועל דולפין נתניה" },
            new Club { Id = Raanana, Name = "ע.ל.ה עצמה רעננה" },
            new Club { Id = Shoham, Name = "מכבי שוהם" });
        for (var i = 1; i <= 7; i++)
        {
            db.Swimmers.Add(new Swimmer
            {
                Id = i, LastName = $"S{i}", ClubId = Dolphin, ClubLocked = lockFirst && i == 1,
                LogligId = i == 6 ? null : 100 + i,
                LogligIdStatus = i == 6 ? null : i == 7 ? "Suggested" : "Verified",
            });
            db.SwimmerClubCandidates.Add(new SwimmerClubCandidate
            {
                SwimmerId = i, FromClubId = Dolphin, ToClubId = Raanana, Source = "import",
                LastSwimDate = new DateOnly(2026, 11, 5),
            });
        }
        await db.SaveChangesAsync();
    }

    private static FakeLoglig Loglig() => new(new()
    {
        [101] = "ע.ל.ה עצמה רעננה",
        [102] = "הפועל דולפין נתניה",
        [103] = "ארכיון איגוד",
        [104] = "מכבי שוהם",
        [107] = "ע.ל.ה עצמה רעננה",
    });

    [Fact]
    public async Task CheckPending_AppliesOnlyConfirmed_OthersWaitWithVerdict()
    {
        await using var db = CreateDb(nameof(CheckPending_AppliesOnlyConfirmed_OthersWaitWithVerdict));
        await SeedAsync(db);
        var loglig = Loglig();

        var report = await Service(db, loglig).CheckPendingAsync(SwimmerClubCheckScope.AllPending);

        Assert.Equal((7, 1), (report.Checked, report.Applied));
        Assert.Equal(5, report.Requests); // без loglig-id и с непроверенным id — в сеть не ходим
        var cards = await db.Swimmers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.ClubId);
        Assert.Equal(Raanana, cards[1]);
        Assert.All(Enumerable.Range(2, 6), i => Assert.Equal(Dolphin, cards[i]));

        var log = Assert.Single(db.SwimmerClubChanges);
        Assert.Equal((1, (int?)Dolphin, (int?)Raanana, "loglig"), (log.SwimmerId, log.FromClubId, log.ToClubId, log.Source));
        Assert.Equal(new DateOnly(2026, 11, 5), log.LastSwimDate);

        var left = await db.SwimmerClubCandidates.AsNoTracking().ToDictionaryAsync(c => c.SwimmerId);
        Assert.False(left.ContainsKey(1)); // применённый кандидат удалён
        Assert.Equal(SwimmerClubLogligVerdicts.StillOld, left[2].Verdict);
        Assert.Equal(SwimmerClubLogligVerdicts.Archive, left[3].Verdict);
        Assert.Equal(SwimmerClubLogligVerdicts.OtherClub, left[4].Verdict);
        Assert.Equal("מכבי שוהם", left[4].LogligClubName);
        Assert.Equal(SwimmerClubLogligVerdicts.Unavailable, left[5].Verdict);
        Assert.Null(left[5].CheckedAt);
        Assert.Equal(SwimmerClubLogligVerdicts.NoLogligId, left[6].Verdict);
        Assert.Equal(SwimmerClubLogligVerdicts.NoLogligId, left[7].Verdict); // Suggested — не подтверждён
    }

    [Fact]
    public async Task SecondRunSameDay_UsesCache_RetriesOnlyFailed()
    {
        await using var db = CreateDb(nameof(SecondRunSameDay_UsesCache_RetriesOnlyFailed));
        await SeedAsync(db);
        var loglig = Loglig();
        var svc = Service(db, loglig);
        await svc.CheckPendingAsync(SwimmerClubCheckScope.AllPending);
        loglig.Calls.Clear();

        var again = await svc.CheckPendingAsync(SwimmerClubCheckScope.AllPending);

        Assert.Equal([105], loglig.Calls);      // только та, что не открылась
        Assert.Equal(3, again.FromCache);       // 102, 103, 104 — из кэша
        Assert.Equal(0, again.Applied);
    }

    [Fact]
    public async Task UncheckedScope_SkipsAlreadyChecked()
    {
        await using var db = CreateDb(nameof(UncheckedScope_SkipsAlreadyChecked));
        await SeedAsync(db);
        (await db.SwimmerClubCandidates.SingleAsync(c => c.SwimmerId == 2)).Verdict = SwimmerClubLogligVerdicts.StillOld;
        await db.SaveChangesAsync();

        var report = await Service(db, Loglig()).CheckPendingAsync(SwimmerClubCheckScope.Unchecked);

        Assert.Equal(6, report.Checked);
        Assert.DoesNotContain(report.Verdicts, v => v.SwimmerId == 2);
    }

    [Fact]
    public async Task LockedSwimmer_NotChanged_EvenIfConfirmed()
    {
        await using var db = CreateDb(nameof(LockedSwimmer_NotChanged_EvenIfConfirmed));
        await SeedAsync(db, lockFirst: true);

        var report = await Service(db, Loglig()).CheckPendingAsync(SwimmerClubCheckScope.AllPending);

        Assert.Equal(0, report.Applied);
        Assert.Equal(Dolphin, (await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 1)).ClubId);
        Assert.Empty(db.SwimmerClubChanges);
        Assert.DoesNotContain(db.SwimmerClubCandidates, c => c.SwimmerId == 1);
    }

    [Fact]
    public async Task CardEditedMeanwhile_NotOverwritten()
    {
        await using var db = CreateDb(nameof(CardEditedMeanwhile_NotOverwritten));
        await SeedAsync(db);
        (await db.Swimmers.SingleAsync(s => s.Id == 1)).ClubId = Shoham; // админ поправил после заведения кандидата
        await db.SaveChangesAsync();

        var report = await Service(db, Loglig()).CheckPendingAsync(SwimmerClubCheckScope.AllPending);

        Assert.Equal(0, report.Applied);
        Assert.Equal(Shoham, (await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 1)).ClubId);
    }

    [Fact]
    public async Task Preview_ReportsVerdicts_WritesNothing()
    {
        await using var db = CreateDb(nameof(Preview_ReportsVerdicts_WritesNothing));
        await SeedAsync(db);
        db.SwimmerClubCandidates.RemoveRange(db.SwimmerClubCandidates);
        await db.SaveChangesAsync();
        var plans = Enumerable.Range(1, 3)
            .Select(i => new SwimmerClubChangePlan(i, $"S{i}", Dolphin, "D", Raanana, "R", new DateOnly(2026, 11, 5), false))
            .ToList();
        var progress = 0;

        var report = await Service(db, Loglig()).PreviewAsync(plans, (done, _) => progress = done);

        Assert.Equal(
            [SwimmerClubLogligVerdicts.Confirmed, SwimmerClubLogligVerdicts.StillOld, SwimmerClubLogligVerdicts.Archive],
            report.Verdicts.Select(v => v.Verdict));
        Assert.Equal(3, progress);
        Assert.All(report.Verdicts, v => Assert.False(v.Applied));
        Assert.Equal(Dolphin, (await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 1)).ClubId);
        Assert.Empty(db.SwimmerClubChanges);
        Assert.Empty(db.SwimmerClubCandidates);
    }

    [Fact]
    public async Task CheckSwimmer_IgnoresCache_AndApplies()
    {
        await using var db = CreateDb(nameof(CheckSwimmer_IgnoresCache_AndApplies));
        await SeedAsync(db);
        // В кэше вчерашний ответ «Дельфин», а сегодня loglig уже перевёл пловца в Раанану.
        var c = await db.SwimmerClubCandidates.SingleAsync(x => x.SwimmerId == 1);
        (c.Verdict, c.LogligId, c.LogligClubName, c.CheckedAt) =
            (SwimmerClubLogligVerdicts.StillOld, 101, "הפועל דולפין נתניה", DateTime.UtcNow.AddHours(-1));
        await db.SaveChangesAsync();
        var loglig = Loglig();

        var verdict = await Service(db, loglig).CheckSwimmerAsync(1);

        Assert.Equal([101], loglig.Calls);
        Assert.Equal((SwimmerClubLogligVerdicts.Confirmed, true, false), (verdict!.Verdict, verdict.Applied, verdict.FromCache));
        Assert.Equal(Raanana, (await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 1)).ClubId);
        Assert.Null(await Service(db, loglig).CheckSwimmerAsync(999));
    }
}
