using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Штамповка loglig-id пловцам по протоколу соревнования (после импорта).
///
/// Правила, ради которых тесты и написаны: уже привязанного не трогаем, тёзок не гадаем,
/// занятый id не отбираем.
/// </summary>
public class LogligStampServiceTests
{
    private const int OrgCompId = 16713;
    private const int CompetitionLogligId = 13627;

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class FakeDiscovery(int? logligId = CompetitionLogligId) : ICompetitionDiscoveryService
    {
        public Task<IReadOnlyList<DiscoveredCompetitionDto>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DiscoveredCompetitionDto>>(
            [
                new(1, OrgCompId, "Meet", new DateTime(2025, 12, 23, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2025, 12, 23, 0, 0, 0, DateTimeKind.Utc), null, logligId, "imported",
                    DateTime.UtcNow, DateTime.UtcNow, null, null, null, null)
            ]);

        public Task<DiscoverySyncResult> SyncAsync(int? year = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<DiscoveredCompetitionDto?> RefreshDetailsAsync(int id, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> SetStatusAsync(int id, string status, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SetDisciplineAsync(int id, string discipline, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<DiscoveryBackfillRow>> BackfillImportedOrgCompIdsAsync(bool apply, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> AddLanguagesAsync(int id, IEnumerable<string> languages, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SetLastErrorAsync(int id, string? error, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SetEmptySourceAsync(int id, bool empty, string by, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class FakeLoglig(params LogligParticipant[] participants) : ILogligClient
    {
        public IReadOnlyCollection<string>? AskedFor { get; private set; }

        public Task<IReadOnlyList<LogligParticipant>> GetCompetitionParticipantsAsync(
            int competitionLogligId, IReadOnlyCollection<string>? wanted = null,
            int maxEvents = 60, CancellationToken ct = default)
        {
            AskedFor = wanted;
            return Task.FromResult<IReadOnlyList<LogligParticipant>>(participants);
        }

        public Task<LogligPlayerCard?> GetPlayerCardAsync(int logligId, int? seasonId = null, CancellationToken ct = default)
            => Task.FromResult<LogligPlayerCard?>(null);
        public string BuildPublicProfileUrl(int logligId, int? seasonId = null, bool resultsTab = false)
            => $"https://loglig.com:2053/Players/Details/{logligId}";
        public Task<int?> GetCompetitionSeasonIdAsync(int competitionLogligId, CancellationToken ct = default)
            => Task.FromResult<int?>(1715);
        public Task<LogligCompetitionStanding?> GetCompetitionStandingAsync(
            int logligId, int scaleSampleEvents = 12, CancellationToken ct = default)
            => Task.FromResult<LogligCompetitionStanding?>(null);
        public Task<LogligRegulationDoc?> GetRegulationAsync(int logligId, CancellationToken ct = default)
            => Task.FromResult<LogligRegulationDoc?>(null);
    }

    /// <summary>Пловец + его результат на этом соревновании (иначе он «не участник»).</summary>
    private static async Task<SwimmDbContext> DbWithAsync(string name, params Swimmer[] swimmers)
    {
        var db = CreateDb(name);
        var competition = new Competition
        {
            Id = 500, Name = "Meet", Date = "23/12/2025", OrgCompId = OrgCompId, PoolType = "25m"
        };
        db.Competitions.Add(competition);
        db.Swimmers.AddRange(swimmers);

        var resultId = 1;
        foreach (var s in swimmers)
            db.Results.Add(new ResultRecord
            {
                Id = resultId++, CompetitionId = competition.Id, SwimmerId = s.Id,
                ClubId = 1, StyleId = 1, CompetitionDate = new DateTime(2025, 12, 23, 0, 0, 0, DateTimeKind.Utc),
                Distance = "50", Gender = "female", TimeOriginal = "00:25.62"
            });

        await db.SaveChangesAsync();
        return db;
    }

    private static Swimmer S(int id, string first, string last, int year, int? logligId = null) =>
        new() { Id = id, FirstName = first, LastName = last, BirthYear = year, LogligId = logligId };

    private static LogligStampService Service(SwimmDbContext db, ILogligClient loglig, int? compLogligId = CompetitionLogligId) =>
        new(db, new FakeDiscovery(compLogligId), loglig, NullLogger<LogligStampService>.Instance);

    // ── тесты ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StampsUnlinkedSwimmer_FromProtocol()
    {
        var db = await DbWithAsync(nameof(StampsUnlinkedSwimmer_FromProtocol), S(7, "אמילי", "גולוס", 2008));
        var loglig = new FakeLoglig(new LogligParticipant(109926, "אמילי גולוס", 2008));

        var report = await Service(db, loglig).StampFromProtocolAsync(OrgCompId);

        Assert.Equal(1, report.Stamped);
        var swimmer = await db.Swimmers.FindAsync(7);
        Assert.Equal(109926, swimmer!.LogligId);
        Assert.Equal("Verified", swimmer.LogligIdStatus);
        // Источник отличает автоматическую привязку от ручной — по нему её потом и находят.
        Assert.Equal("protocol", swimmer.LogligIdSource);
    }

    [Fact]
    public async Task AsksOnlyForUnlinkedSwimmers()
    {
        // Обход заплывов дорогой: ищем только тех, кого ещё не привязали.
        var db = await DbWithAsync(nameof(AsksOnlyForUnlinkedSwimmers),
            S(7, "אמילי", "גולוס", 2008), S(8, "מיכאל", "בכר", 2011, logligId: 555));
        var loglig = new FakeLoglig(new LogligParticipant(109926, "אמילי גולוס", 2008));

        await Service(db, loglig).StampFromProtocolAsync(OrgCompId);

        Assert.NotNull(loglig.AskedFor);
        Assert.Single(loglig.AskedFor!);
    }

    [Fact]
    public async Task DoesNotTouchAlreadyLinked()
    {
        var db = await DbWithAsync(nameof(DoesNotTouchAlreadyLinked), S(7, "אמילי", "גולוס", 2008, logligId: 111));
        var loglig = new FakeLoglig(new LogligParticipant(109926, "אמילי גולוס", 2008));

        var report = await Service(db, loglig).StampFromProtocolAsync(OrgCompId);

        Assert.Equal(0, report.Stamped);
        Assert.Equal(1, report.AlreadyLinked);
        Assert.Equal(111, (await db.Swimmers.FindAsync(7))!.LogligId);
    }

    [Fact]
    public async Task SkipsNamesakes()
    {
        // Двое с одним именем и годом: привязать не тому хуже, чем не привязать.
        var db = await DbWithAsync(nameof(SkipsNamesakes),
            S(7, "אמילי", "גולוס", 2008), S(8, "אמילי", "גולוס", 2008));
        var loglig = new FakeLoglig(new LogligParticipant(109926, "אמילי גולוס", 2008));

        var report = await Service(db, loglig).StampFromProtocolAsync(OrgCompId);

        Assert.Equal(0, report.Stamped);
        Assert.Single(report.Skipped);
        Assert.Contains("тёзки", report.Skipped[0]);
    }

    [Fact]
    public async Task DoesNotStealIdFromAnotherSwimmer()
    {
        // Id уже у другого пловца — это симптом дубля, разбирается дедупом, а не отбором id.
        var db = await DbWithAsync(nameof(DoesNotStealIdFromAnotherSwimmer), S(7, "אמילי", "גולוס", 2008));
        db.Swimmers.Add(S(99, "אמילי", "גולוס", 2009, logligId: 109926));
        await db.SaveChangesAsync();

        var loglig = new FakeLoglig(new LogligParticipant(109926, "אמילי גולוס", 2008));
        var report = await Service(db, loglig).StampFromProtocolAsync(OrgCompId);

        Assert.Equal(0, report.Stamped);
        Assert.Contains("дубль", Assert.Single(report.Skipped));
        Assert.Null((await db.Swimmers.FindAsync(7))!.LogligId);
    }

    [Fact]
    public async Task CountsThoseMissingFromProtocol()
    {
        var db = await DbWithAsync(nameof(CountsThoseMissingFromProtocol), S(7, "אמילי", "גולוס", 2008));
        var report = await Service(db, new FakeLoglig(new LogligParticipant(1, "מישהו אחר", 2008)))
            .StampFromProtocolAsync(OrgCompId);

        Assert.Equal(0, report.Stamped);
        Assert.Equal(1, report.NotFound);
    }

    [Fact]
    public async Task DoesNothing_WhenCompetitionHasNoLogligId()
    {
        var db = await DbWithAsync(nameof(DoesNothing_WhenCompetitionHasNoLogligId), S(7, "אמילי", "גולוס", 2008));
        var report = await Service(db, new FakeLoglig(), compLogligId: null).StampFromProtocolAsync(OrgCompId);

        Assert.Equal(0, report.Stamped);
        Assert.Contains("нет loglig-id", report.Message);
    }

    [Fact]
    public async Task MatchesRegardlessOfTokenOrder()
    {
        // На сайте «имя фамилия», у нас поля раздельные и порядок другой.
        var db = await DbWithAsync(nameof(MatchesRegardlessOfTokenOrder), S(7, "אמילי", "גולוס", 2008));
        var loglig = new FakeLoglig(new LogligParticipant(109926, "גולוס אמילי", 2008));

        Assert.Equal(1, (await Service(db, loglig).StampFromProtocolAsync(OrgCompId)).Stamped);
    }
}
