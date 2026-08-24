using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сопоставление пловца подозрительного заплыва с базой: от него зависит, что покажет
/// превью — вердикт по карточке loglig, поле для привязки id или «пловца ещё нет».
///
/// Ключевая ловушка — ТЁЗКИ: гадать нельзя, иначе id уедет не тому человеку.
/// </summary>
public class PreviewRecordCheckServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    // ── фейки ─────────────────────────────────────────────────────────────────

    private sealed class FakePreviews(DiscoveryPreviewEntry? entry) : IDiscoveryPreviewService
    {
        public TimeSpan EntryLifetime => TimeSpan.FromMinutes(60);
        public DiscoveryPreviewEntry? GetEntry(Guid previewId) => entry;
        public void RemoveEntry(Guid previewId) { }
        public Task<DiscoveryPreviewResult> PreviewAsync(int discoveredId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<DiscoveryProtocolPdf> FetchProtocolAsync(
            int discoveredId, string language, bool refreshIfMissing, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeDiscovery : ICompetitionDiscoveryService
    {
        public Task<IReadOnlyList<DiscoveredCompetitionDto>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DiscoveredCompetitionDto>>(
            [
                new(1, 16700, "Meet", DateTime.UtcNow, DateTimeUtc(), null, 12345, "new",
                    DateTime.UtcNow, DateTime.UtcNow, null, null, null, null)
            ]);

        private static DateTime DateTimeUtc() => DateTime.UtcNow;

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

    /// <summary>Карточка задаётся тестом; сезон соревнования — 1605, как в живом примере.
    /// Участники — то, что «печатает протокол» (ссылки на карточки на странице заплыва).</summary>
    private sealed class FakeLoglig(LogligPlayerCard? card, params LogligParticipant[] participants) : ILogligClient
    {
        public int? AskedSeasonId { get; private set; }

        public Task<LogligPlayerCard?> GetPlayerCardAsync(
            int logligId, int? seasonId = null, CancellationToken ct = default)
        {
            AskedSeasonId = seasonId;
            return Task.FromResult(card);
        }

        public string BuildPublicProfileUrl(int logligId, int? seasonId = null, bool resultsTab = false)
            => $"https://loglig.com:2053/Players/Details/{logligId}?seasonId={seasonId}"
               + (resultsTab ? "&tab=results" : "");

        public Task<int?> GetCompetitionSeasonIdAsync(int competitionLogligId, CancellationToken ct = default)
            => Task.FromResult<int?>(1605);

        public Task<LogligCompetitionStanding?> GetCompetitionStandingAsync(
            int logligId, int scaleSampleEvents = 12, CancellationToken ct = default)
            => Task.FromResult<LogligCompetitionStanding?>(null);

        public Task<LogligRegulationDoc?> GetRegulationAsync(int logligId, CancellationToken ct = default)
            => Task.FromResult<LogligRegulationDoc?>(null);

        public Task<IReadOnlyList<LogligParticipant>> GetCompetitionParticipantsAsync(
            int competitionLogligId, IReadOnlyCollection<string>? wanted = null,
            int maxEvents = 60, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LogligParticipant>>(participants);
    }

    // ── сборка ────────────────────────────────────────────────────────────────

    private static DiscoveryPreviewEntry Entry(string swimmerName = "מיכאל בכר", int birthYear = 2011) =>
        new(
            new ParsedCompetition { Format = "IsrOrg", ResultsJson = "[]", ResultCount = 1 },
            "file.pdf", 1, null,
            new ImportRecordPreviewDto
            {
                Count = 1,
                Rows =
                [
                    new ImportRecordPreviewRow
                    {
                        RowIndex = 3, Kind = "Age 14 record", SwimmerName = swimmerName,
                        StyleName = "backstroke", Distance = "50", Gender = "male",
                        Time = "00:25.62", RecordTime = "25.62", BirthYear = birthYear, PoolType = "25m"
                    }
                ]
            });

    private static async Task<SwimmDbContext> DbWithAsync(string name, params Swimmer[] swimmers)
    {
        var db = CreateDb(name);
        db.Swimmers.AddRange(swimmers);
        await db.SaveChangesAsync();
        return db;
    }

    private static Swimmer S(int id, string first, string last, int year, int? logligId = null, string? gender = null) =>
        new() { Id = id, FirstName = first, LastName = last, BirthYear = year, LogligId = logligId, Gender = gender };

    private static IMemoryCache Cache() => new MemoryCache(new MemoryCacheOptions());

    private static PreviewRecordCheckService Service(
        SwimmDbContext db, DiscoveryPreviewEntry entry, ILogligClient loglig) =>
        new(db, new FakePreviews(entry), new FakeDiscovery(), loglig, Cache());

    private static LogligPlayerCard Card(params LogligResultRow[] rows) =>
        new("מיכאל בכר", 2011, "male", "Hapoel", rows);

    // ── тесты ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reports_NoSwimmerInDb()
    {
        // Обычное дело: соревнование ещё не импортировано, пловца в базе нет.
        var db = await DbWithAsync(nameof(Reports_NoSwimmerInDb));
        var rows = await Service(db, Entry(), new FakeLoglig(null)).CheckAsync(Guid.NewGuid());

        var row = Assert.Single(rows);
        Assert.Equal(PreviewSwimmerMatch.None, row.Match);
        Assert.Null(row.SwimmerId);
    }

    [Fact]
    public async Task MatchesSwimmer_IgnoringTokenOrder()
    {
        // В протоколе «имя фамилия», в базе поля раздельные и в другом порядке — это ОДИН человек.
        var db = await DbWithAsync(nameof(MatchesSwimmer_IgnoringTokenOrder), S(7, "מיכאל", "בכר", 2011));
        var rows = await Service(db, Entry(), new FakeLoglig(null)).CheckAsync(Guid.NewGuid());

        var row = Assert.Single(rows);
        Assert.Equal(PreviewSwimmerMatch.One, row.Match);
        Assert.Equal(7, row.SwimmerId);
        Assert.Null(row.LogligId);
        Assert.Contains("не привязан к loglig", row.Message);
    }

    [Fact]
    public async Task DoesNotGuess_WhenNamesakesExist()
    {
        var db = await DbWithAsync(nameof(DoesNotGuess_WhenNamesakesExist),
            S(7, "מיכאל", "בכר", 2011), S(8, "מיכאל", "בכר", 2011, logligId: 999));

        var rows = await Service(db, Entry(), new FakeLoglig(null)).CheckAsync(Guid.NewGuid());

        var row = Assert.Single(rows);
        Assert.Equal(PreviewSwimmerMatch.Many, row.Match);
        Assert.Null(row.SwimmerId);
        Assert.Null(row.LogligId);
    }

    [Fact]
    public async Task DifferentBirthYearIsNotTheSamePerson()
    {
        var db = await DbWithAsync(nameof(DifferentBirthYearIsNotTheSamePerson), S(7, "מיכאל", "בכר", 2009));
        var rows = await Service(db, Entry(birthYear: 2011), new FakeLoglig(null)).CheckAsync(Guid.NewGuid());

        Assert.Equal(PreviewSwimmerMatch.None, Assert.Single(rows).Match);
    }

    [Fact]
    public async Task ChecksCard_AndLinksWithCompetitionSeason()
    {
        var db = await DbWithAsync(nameof(ChecksCard_AndLinksWithCompetitionSeason),
            S(7, "מיכאל", "בכר", 2011, logligId: 177040, gender: "male"));

        var card = Card(new LogligResultRow("50 גב", "50", "backstroke", false, 25, "00:25.62", 25_620,
            new DateTime(2025, 12, 5, 0, 0, 0, DateTimeKind.Utc), "Meet"));
        var loglig = new FakeLoglig(card);

        var row = Assert.Single(await Service(db, Entry(), loglig).CheckAsync(Guid.NewGuid()));

        Assert.Equal(177040, row.LogligId);
        Assert.Equal(RecordCheckVerdict.Confirms, row.Verdict);
        // Сезон — соревнования, а не из конфига; вкладка результатов открыта сразу.
        Assert.Equal(1605, loglig.AskedSeasonId);
        Assert.Contains("seasonId=1605", row.LogligUrl);
        Assert.Contains("tab=results", row.LogligUrl);
    }

    [Fact]
    public async Task RecommendsSuspect_WhenCardContradicts()
    {
        var db = await DbWithAsync(nameof(RecommendsSuspect_WhenCardContradicts),
            S(7, "מיכאל", "בכר", 2011, logligId: 177040));

        var card = Card(new LogligResultRow("50 גב", "50", "backstroke", false, 25, "00:31.10", 31_100,
            new DateTime(2025, 12, 5, 0, 0, 0, DateTimeKind.Utc), "Meet"));

        var row = Assert.Single(await Service(db, Entry(), new FakeLoglig(card)).CheckAsync(Guid.NewGuid()));

        Assert.Equal(RecordCheckVerdict.Contradicts, row.Verdict);
        Assert.Contains("пометить заплыв сомнительным", row.Message);
    }

    [Fact]
    public async Task ReturnsNothing_WhenPreviewExpired()
    {
        var db = await DbWithAsync(nameof(ReturnsNothing_WhenPreviewExpired));
        var service = new PreviewRecordCheckService(
            db, new FakePreviews(null), new FakeDiscovery(), new FakeLoglig(null), Cache());

        Assert.Empty(await service.CheckAsync(Guid.NewGuid()));
    }

    // ── loglig-id из самого протокола ─────────────────────────────────────────

    [Fact]
    public async Task ChecksSwimmerMissingFromDb_UsingIdFromProtocol()
    {
        // Главный случай, ради которого это делалось: пловца в базе НЕТ (соревнование ещё не
        // импортировано), но протокол знает его карточку — время проверить можно.
        var db = await DbWithAsync(nameof(ChecksSwimmerMissingFromDb_UsingIdFromProtocol));

        var card = Card(new LogligResultRow("50 גב", "50", "backstroke", false, 25, "00:31.10", 31_100,
            new DateTime(2025, 12, 5, 0, 0, 0, DateTimeKind.Utc), "Meet"));
        var loglig = new FakeLoglig(card, new LogligParticipant(109926, "מיכאל בכר", 2011));

        var row = Assert.Single(await Service(db, Entry(), loglig).CheckAsync(Guid.NewGuid()));

        Assert.Equal(PreviewSwimmerMatch.None, row.Match);
        Assert.Equal(RecordCheckVerdict.Contradicts, row.Verdict);      // время сверено
        Assert.Equal(109926, row.SuggestedLogligId);                    // id известен
        Assert.Null(row.SwimmerId);                                     // но привязывать некому
        Assert.Contains("проставится при импорте", row.Message);
        Assert.Contains("seasonId=1605", row.LogligUrl);
    }

    [Fact]
    public async Task OffersOneClickBinding_WhenProtocolKnowsTheId()
    {
        // Пловец в базе есть и не привязан: id вводить руками не надо — он в протоколе.
        var db = await DbWithAsync(nameof(OffersOneClickBinding_WhenProtocolKnowsTheId),
            S(7, "מיכאל", "בכר", 2011));

        var card = Card(new LogligResultRow("50 גב", "50", "backstroke", false, 25, "00:25.62", 25_620,
            new DateTime(2025, 12, 5, 0, 0, 0, DateTimeKind.Utc), "Meet"));
        var loglig = new FakeLoglig(card, new LogligParticipant(109926, "בכר מיכאל", 2011));

        var row = Assert.Single(await Service(db, Entry(), loglig).CheckAsync(Guid.NewGuid()));

        Assert.Equal(PreviewSwimmerMatch.One, row.Match);
        Assert.Equal(7, row.SwimmerId);
        Assert.Null(row.LogligId);                    // в базе связи ещё нет
        Assert.Equal(109926, row.SuggestedLogligId);  // но кнопка «привязать #109926» уже есть
        Assert.Equal(RecordCheckVerdict.Confirms, row.Verdict);
    }

    [Fact]
    public async Task NamesakesAreStillChecked_ButNotBound()
    {
        // Тёзки: привязывать нельзя (id уедет не тому), а время сверить — можно.
        var db = await DbWithAsync(nameof(NamesakesAreStillChecked_ButNotBound),
            S(7, "מיכאל", "בכר", 2011), S(8, "מיכאל", "בכר", 2011));

        var card = Card(new LogligResultRow("50 גב", "50", "backstroke", false, 25, "00:25.62", 25_620,
            new DateTime(2025, 12, 5, 0, 0, 0, DateTimeKind.Utc), "Meet"));
        var loglig = new FakeLoglig(card, new LogligParticipant(109926, "מיכאל בכר", 2011));

        var row = Assert.Single(await Service(db, Entry(), loglig).CheckAsync(Guid.NewGuid()));

        Assert.Equal(PreviewSwimmerMatch.Many, row.Match);
        Assert.Null(row.SwimmerId);
        Assert.Equal(RecordCheckVerdict.Confirms, row.Verdict);
        Assert.Contains("тёзки", row.Message);
    }

    [Fact]
    public async Task DbLinkWins_OverProtocolId()
    {
        // Уже привязанного не переспрашиваем: связь в базе — решение человека.
        var db = await DbWithAsync(nameof(DbLinkWins_OverProtocolId),
            S(7, "מיכאל", "בכר", 2011, logligId: 555));

        var loglig = new FakeLoglig(Card(), new LogligParticipant(109926, "מיכאל בכר", 2011));
        var row = Assert.Single(await Service(db, Entry(), loglig).CheckAsync(Guid.NewGuid()));

        Assert.Equal(555, row.LogligId);
        Assert.Null(row.SuggestedLogligId);
    }
}
