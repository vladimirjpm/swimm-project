using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Общие пловцы пары клубов и их клуб на loglig (Р112): чтение без сети, кнопка «Проверить в loglig»
/// ходит только за несвежими ответами и сохраняет их в общее место, которое видит и сверка
/// кандидатов на смену клуба (Р98); сводка доезжает до таблицы кандидатов в дубли.
/// </summary>
public class ClubPairLogligServiceTests
{
    private const int A = 509, B = 1455, Raanana = 449;

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

        public string BuildPublicProfileUrl(int logligId, int? seasonId = null, bool resultsTab = false) =>
            $"https://loglig.com:2053/Players/Details/{logligId}?seasonId=1786";
        public Task<int?> GetCompetitionSeasonIdAsync(int competitionLogligId, CancellationToken ct = default) => Task.FromResult<int?>(null);
        public Task<LogligCompetitionStanding?> GetCompetitionStandingAsync(int logligId, int scaleSampleEvents = 12, CancellationToken ct = default) =>
            Task.FromResult<LogligCompetitionStanding?>(null);
        public Task<LogligRegulationDoc?> GetRegulationAsync(int logligId, CancellationToken ct = default) =>
            Task.FromResult<LogligRegulationDoc?>(null);
        public Task<IReadOnlyList<LogligParticipant>> GetCompetitionParticipantsAsync(int competitionLogligId,
            IReadOnlyCollection<string>? wanted = null, int maxEvents = 60, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LogligParticipant>>([]);
    }

    /// <summary>loglig: 101 — клуб A, 102 — клуб B, 105 — карточка не открывается.</summary>
    private static FakeLoglig Loglig() => new(new()
    {
        [101] = "הפועל ירושלים",
        [102] = "הפועל ירושלים אומנותית",
        [103] = "ארכיון איגוד",
    });

    private static ClubPairLogligService Service(SwimmDbContext db, ILogligClient loglig) =>
        new(db, loglig, NullLogger<ClubPairLogligService>.Instance, TimeSpan.Zero);

    /// <summary>
    /// Общие пловцы 1–5 (карточка в одном клубе, заплыв за другой): у 1, 2, 5 ответа нет, у 3 —
    /// свежий «архив», у 4 нет loglig-id. Пловец 6 — только клуб A, не общий.
    /// </summary>
    private static async Task SeedAsync(SwimmDbContext db)
    {
        var clubA = new Club { Id = A, Name = "הפועל ירושלים" };
        var clubB = new Club { Id = B, Name = "הפועל ירושלים אומנותית" };
        db.Clubs.AddRange(clubA, clubB, new Club { Id = Raanana, Name = "ע.ל.ה עצמה רעננה" });
        var comp = new Competition { Name = "Meet", Date = "01/06/2026", PoolType = "25m" };
        var style = new Style { Name = "Freestyle" };
        db.AddRange(comp, style);
        for (var i = 1; i <= 6; i++)
        {
            var s = new Swimmer
            {
                Id = i, LastName = $"כהן{i}", FirstName = "דנה", BirthYear = 2012,
                ClubId = i == 2 ? B : A,
                LogligId = i == 4 ? null : 100 + i, LogligIdStatus = i == 4 ? null : "Verified",
            };
            db.Swimmers.Add(s);
            if (i == 6) continue;
            db.Results.Add(new ResultRecord
            {
                Swimmer = s, Competition = comp, Style = style, Distance = "50",
                Club = i == 2 ? clubA : clubB, Gender = "female", CompetitionDate = new DateTime(2026, 6, 1),
            });
        }
        db.SwimmerLogligClubs.Add(new SwimmerLogligClub
        {
            SwimmerId = 3, LogligId = 103, ClubName = "ארכיון איגוד", CheckedAt = DateTime.UtcNow.AddHours(-2),
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_ReadsStoredAnswers_NoNetwork()
    {
        await using var db = CreateDb(nameof(Get_ReadsStoredAnswers_NoNetwork));
        await SeedAsync(db);
        var loglig = Loglig();

        var r = await Service(db, loglig).GetAsync(A, B);

        Assert.NotNull(r);
        Assert.Empty(loglig.Calls);
        Assert.Equal([1, 2, 3, 4, 5], r!.Swimmers.Select(s => s.SwimmerId).Order());
        Assert.Equal((0, 0, 1, 1, 3), (r.Summary.ClubA, r.Summary.ClubB, r.Summary.Archive, r.Summary.NoLogligId, r.Summary.Unchecked));
        var s3 = r.Swimmers.Single(s => s.SwimmerId == 3);
        Assert.Equal("ארכיון איגוד", s3.LogligClubName);
        Assert.Equal("https://loglig.com:2053/Players/Details/103?seasonId=1786", s3.LogligUrl);
        Assert.Null(r.Swimmers.Single(s => s.SwimmerId == 4).LogligUrl);
        Assert.Equal("הפועל ירושלים", s3.CardClubName);
    }

    [Fact]
    public async Task Check_FetchesOnlyStale_SavesAnswers()
    {
        await using var db = CreateDb(nameof(Check_FetchesOnlyStale_SavesAnswers));
        await SeedAsync(db);
        var loglig = Loglig();

        var r = await Service(db, loglig).CheckAsync(A, B, maxRequests: 50);

        // 3 — свежий ответ, 4 — без loglig-id: в сеть только за 1, 2, 5.
        Assert.Equal([101, 102, 105], loglig.Calls.Order());
        Assert.Equal((3, 1, 0), (r!.Requests, r.Failed, r.Remaining));
        Assert.Equal((1, 1, 1, 1, 1), (r.Summary.ClubA, r.Summary.ClubB, r.Summary.Archive, r.Summary.NoLogligId, r.Summary.Unchecked));
        var saved = await db.SwimmerLogligClubs.AsNoTracking().OrderBy(x => x.SwimmerId).ToListAsync();
        Assert.Equal([1, 2, 3], saved.Select(x => x.SwimmerId));
        Assert.Equal("הפועל ירושלים אומנותית", saved[1].ClubName);

        // Повторное нажатие в те же сутки: только та, что не открылась.
        loglig.Calls.Clear();
        await Service(db, loglig).CheckAsync(A, B, maxRequests: 50);
        Assert.Equal([105], loglig.Calls);
    }

    [Fact]
    public async Task Check_RespectsLimit_ReportsRemaining()
    {
        await using var db = CreateDb(nameof(Check_RespectsLimit_ReportsRemaining));
        await SeedAsync(db);
        var loglig = Loglig();

        var r = await Service(db, loglig).CheckAsync(A, B, maxRequests: 1);

        Assert.Single(loglig.Calls);
        Assert.Equal((1, 2), (r!.Requests, r.Remaining));
    }

    [Fact]
    public async Task UnknownClub_Null()
    {
        await using var db = CreateDb(nameof(UnknownClub_Null));
        await SeedAsync(db);
        Assert.Null(await Service(db, Loglig()).GetAsync(A, 99999));
        Assert.Null(await Service(db, Loglig()).GetAsync(A, A));
    }

    [Fact]
    public async Task DedupCandidates_SwimmersPairCarriesSummary()
    {
        await using var db = CreateDb(nameof(DedupCandidates_SwimmersPairCarriesSummary));
        await SeedAsync(db);
        var loglig = Loglig();

        var report = await new ClubDedupService(db, Service(db, loglig)).FindCandidatesAsync();

        var cand = Assert.Single(report.Candidates, c => c.Heuristic == "swimmers");
        Assert.Equal(5, cand.SharedSwimmers);
        Assert.NotNull(cand.Loglig);
        Assert.Equal(5, cand.Loglig!.Total);
        Assert.Equal(1, cand.Loglig.Archive);
        Assert.Empty(loglig.Calls); // страница кандидатов в сеть не ходит
    }

    [Fact]
    public async Task CandidateCheck_UsesSharedAnswer_AndWritesIt()
    {
        await using var db = CreateDb(nameof(CandidateCheck_UsesSharedAnswer_AndWritesIt));
        await SeedAsync(db);
        // Кандидаты на смену клуба: 1 — A → B (свежего общего ответа нет), 3 — A → B (общий ответ «архив»).
        db.SwimmerClubCandidates.AddRange(
            new SwimmerClubCandidate { SwimmerId = 1, FromClubId = A, ToClubId = B, Source = "import", LastSwimDate = new DateOnly(2026, 6, 1) },
            new SwimmerClubCandidate { SwimmerId = 3, FromClubId = A, ToClubId = B, Source = "import", LastSwimDate = new DateOnly(2026, 6, 1) });
        await db.SaveChangesAsync();
        var loglig = Loglig();

        var report = await new SwimmerClubLogligCheckService(db, loglig,
            NullLogger<SwimmerClubLogligCheckService>.Instance, TimeSpan.Zero).CheckPendingAsync(SwimmerClubCheckScope.AllPending);

        // 3 — из общего места, без сети; 1 — сеть, ответ лёг в общее место.
        Assert.Equal([101], loglig.Calls);
        Assert.Equal(1, report.FromCache);
        var c3 = await db.SwimmerClubCandidates.AsNoTracking().SingleAsync(c => c.SwimmerId == 3);
        Assert.Equal(SwimmerClubLogligVerdicts.Archive, c3.Verdict);
        Assert.Equal("ארכיון איגוד", c3.LogligClubName);
        var shared1 = await db.SwimmerLogligClubs.AsNoTracking().SingleAsync(x => x.SwimmerId == 1);
        Assert.Equal((101, "הפועל ירושלים"), (shared1.LogligId, shared1.ClubName));
    }
}
