using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Оркестрация привязки Loglig ID (docs/tasks/loglig-admin-ui-sonnet.md, шаг 5): автопоиск+сверка,
/// ручная привязка, уникальность, unlink. Фейки интерфейсов — простые классы-стабы (Moq в проекте нет).
/// </summary>
public class LogligLinkServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static Swimmer S(string last, string first, int year, string? status = null, int? logligId = null) =>
        new()
        {
            LastName = last, FirstName = first, BirthYear = year,
            LogligId = logligId, LogligIdStatus = status,
            LogligIdSource = status != null ? "admin" : null,
            LogligIdVerifiedAt = status == "Verified" ? DateTime.UtcNow : null,
        };

    /// <summary>Фейк поиска кандидатов: список ID из конструктора (или "не сконфигурирован").</summary>
    private sealed class FakeSearchProvider(IReadOnlyList<int>? ids, bool configured = true) : ICandidateSearchProvider
    {
        public bool IsConfigured => configured;
        public Task<IReadOnlyList<int>> FindCandidatesAsync(string lastNameHe, string firstNameHe, CancellationToken ct = default)
            => Task.FromResult(ids ?? (IReadOnlyList<int>)[]);
    }

    /// <summary>Фейк клиента карточек: словарь id → карточка (null/нет ключа — карточка недоступна).</summary>
    private sealed class FakeLogligClient(IReadOnlyDictionary<int, LogligPlayerCard?> cards) : ILogligClient
    {
        public Task<LogligPlayerCard?> GetPlayerCardAsync(int logligId, CancellationToken ct = default)
            => Task.FromResult(cards.TryGetValue(logligId, out var card) ? card : null);
    }

    /// <summary>Фейк сверки: решение по id карточки, заданное словарём в тесте.</summary>
    private sealed class FakeMatchService(Func<LogligPlayerCard, LogligMatchDecision> decide) : ILogligMatchService
    {
        public LogligMatchReport Match(LogligPlayerCard card, int swimmerBirthYear, string? swimmerClubName, IReadOnlyList<LocalResultKey> localResults)
            => new(decide(card), true, false, 0, []);
    }

    private static LogligPlayerCard Card(string name) => new(name, 2017, "F", "Club", []);

    private static LogligLinkService Service(
        SwimmDbContext db,
        ICandidateSearchProvider? search = null,
        ILogligClient? client = null,
        ILogligMatchService? match = null) =>
        new(db,
            search ?? new FakeSearchProvider([]),
            client ?? new FakeLogligClient(new Dictionary<int, LogligPlayerCard?>()),
            match ?? new FakeMatchService(_ => LogligMatchDecision.Candidate),
            NullLogger<LogligLinkService>.Instance);

    [Fact]
    public async Task FindAndLinkAsync_OneAutoVerifyCandidate_Links()
    {
        await using var db = CreateDb(nameof(FindAndLinkAsync_OneAutoVerifyCandidate_Links));
        var s = S("כהן", "נועה", 2017);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        var cards = new Dictionary<int, LogligPlayerCard?> { [111] = Card("Noa Cohen") };
        var service = Service(db,
            search: new FakeSearchProvider([111]),
            client: new FakeLogligClient(cards),
            match: new FakeMatchService(_ => LogligMatchDecision.AutoVerify));

        var result = await service.FindAndLinkAsync(s.Id, CancellationToken.None);

        Assert.True(result.Linked);
        Assert.Null(result.Error);
        var reloaded = await db.Swimmers.FindAsync(s.Id);
        Assert.Equal(111, reloaded!.LogligId);
        Assert.Equal("Verified", reloaded.LogligIdStatus);
        Assert.Equal("auto", reloaded.LogligIdSource);
        Assert.NotNull(reloaded.LogligIdVerifiedAt);
    }

    [Fact]
    public async Task FindAndLinkAsync_TwoAutoVerifyCandidates_NotLinked_BothReturned()
    {
        await using var db = CreateDb(nameof(FindAndLinkAsync_TwoAutoVerifyCandidates_NotLinked_BothReturned));
        var s = S("כהן", "נועה", 2017);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        var cards = new Dictionary<int, LogligPlayerCard?> { [111] = Card("A"), [222] = Card("B") };
        var service = Service(db,
            search: new FakeSearchProvider([111, 222]),
            client: new FakeLogligClient(cards),
            match: new FakeMatchService(_ => LogligMatchDecision.AutoVerify));

        var result = await service.FindAndLinkAsync(s.Id, CancellationToken.None);

        Assert.False(result.Linked);
        Assert.Null(result.Error);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Null((await db.Swimmers.FindAsync(s.Id))!.LogligId);
    }

    [Fact]
    public async Task FindAndLinkAsync_OnlyCandidateDecisions_NotLinked_ListReturned()
    {
        await using var db = CreateDb(nameof(FindAndLinkAsync_OnlyCandidateDecisions_NotLinked_ListReturned));
        var s = S("כהן", "נועה", 2017);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        var cards = new Dictionary<int, LogligPlayerCard?> { [111] = Card("A") };
        var service = Service(db,
            search: new FakeSearchProvider([111]),
            client: new FakeLogligClient(cards),
            match: new FakeMatchService(_ => LogligMatchDecision.Candidate));

        var result = await service.FindAndLinkAsync(s.Id, CancellationToken.None);

        Assert.False(result.Linked);
        Assert.Single(result.Candidates);
        Assert.Equal(LogligMatchDecision.Candidate, result.Candidates[0].Decision);
    }

    [Fact]
    public async Task FindAndLinkAsync_SearchNotConfigured_ReturnsFlagAndNoCandidates()
    {
        await using var db = CreateDb(nameof(FindAndLinkAsync_SearchNotConfigured_ReturnsFlagAndNoCandidates));
        var s = S("כהן", "נועה", 2017);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        var service = Service(db, search: new FakeSearchProvider(null, configured: false));

        var result = await service.FindAndLinkAsync(s.Id, CancellationToken.None);

        Assert.False(result.Linked);
        Assert.False(result.SearchConfigured);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task SetManualAsync_CardUnavailable_ReturnsError_NotLinked()
    {
        await using var db = CreateDb(nameof(SetManualAsync_CardUnavailable_ReturnsError_NotLinked));
        var s = S("כהן", "נועה", 2017);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        var service = Service(db, client: new FakeLogligClient(new Dictionary<int, LogligPlayerCard?> { [999] = null }));

        var result = await service.SetManualAsync(s.Id, 999, CancellationToken.None);

        Assert.False(result.Linked);
        Assert.NotNull(result.Error);
        Assert.Null((await db.Swimmers.FindAsync(s.Id))!.LogligId);
    }

    [Fact]
    public async Task SetManualAsync_LogligIdAlreadyTaken_ReturnsErrorWithHolderName()
    {
        await using var db = CreateDb(nameof(SetManualAsync_LogligIdAlreadyTaken_ReturnsErrorWithHolderName));
        var holder = S("לוי", "דן", 2016, status: "Verified", logligId: 555);
        var s = S("כהן", "נועה", 2017);
        db.Swimmers.AddRange(holder, s);
        await db.SaveChangesAsync();

        var cards = new Dictionary<int, LogligPlayerCard?> { [555] = Card("Someone") };
        var service = Service(db, client: new FakeLogligClient(cards));

        var result = await service.SetManualAsync(s.Id, 555, CancellationToken.None);

        Assert.False(result.Linked);
        Assert.Contains("555", result.Error);
        Assert.Contains("לוי", result.Error);
        Assert.Null((await db.Swimmers.FindAsync(s.Id))!.LogligId);
    }

    [Fact]
    public async Task FindAndLinkAsync_SwimmerAlreadyVerified_ReturnsError()
    {
        await using var db = CreateDb(nameof(FindAndLinkAsync_SwimmerAlreadyVerified_ReturnsError));
        var s = S("כהן", "נועה", 2017, status: "Verified", logligId: 42);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        var service = Service(db);
        var result = await service.FindAndLinkAsync(s.Id, CancellationToken.None);

        Assert.False(result.Linked);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task SetManualAsync_SwimmerAlreadyVerified_ReturnsError()
    {
        await using var db = CreateDb(nameof(SetManualAsync_SwimmerAlreadyVerified_ReturnsError));
        var s = S("כהן", "נועה", 2017, status: "Verified", logligId: 42);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        var service = Service(db);
        var result = await service.SetManualAsync(s.Id, 100, CancellationToken.None);

        Assert.False(result.Linked);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task UnlinkAsync_ClearsAllSixFields()
    {
        await using var db = CreateDb(nameof(UnlinkAsync_ClearsAllSixFields));
        var s = S("כהן", "נועה", 2017, status: "Verified", logligId: 42);
        s.LogligIdSuggestedByUserId = 7;
        s.LogligIdSuggestedAt = DateTime.UtcNow;
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        var service = Service(db);
        var unlinked = await service.UnlinkAsync(s.Id, CancellationToken.None);

        Assert.True(unlinked);
        var reloaded = await db.Swimmers.FindAsync(s.Id);
        Assert.Null(reloaded!.LogligId);
        Assert.Null(reloaded.LogligIdStatus);
        Assert.Null(reloaded.LogligIdSource);
        Assert.Null(reloaded.LogligIdSuggestedByUserId);
        Assert.Null(reloaded.LogligIdSuggestedAt);
        Assert.Null(reloaded.LogligIdVerifiedAt);
    }

    [Fact]
    public async Task UnlinkAsync_NonExistentSwimmer_ReturnsFalse()
    {
        await using var db = CreateDb(nameof(UnlinkAsync_NonExistentSwimmer_ReturnsFalse));
        var service = Service(db);

        Assert.False(await service.UnlinkAsync(12345, CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_FiltersByStatusAndNameSubstring()
    {
        await using var db = CreateDb(nameof(ListAsync_FiltersByStatusAndNameSubstring));
        db.Swimmers.AddRange(
            S("כהן", "נועה", 2017, status: "Verified", logligId: 1),
            S("לוי", "דן", 2016),
            S("כהן", "יובל", 2015));
        await db.SaveChangesAsync();

        var service = Service(db);

        var linked = await service.ListAsync(null, "linked", 50, CancellationToken.None);
        Assert.Single(linked);
        Assert.Equal("Verified", linked[0].Status);

        var unlinked = await service.ListAsync(null, "unlinked", 50, CancellationToken.None);
        Assert.Equal(2, unlinked.Count);

        var byName = await service.ListAsync("כהן", null, 50, CancellationToken.None);
        Assert.Equal(2, byName.Count);
        Assert.All(byName, r => Assert.Equal("כהן", r.LastName));
    }

    // ── RunBatchAsync (шаг 7) ─────────────────────────────────────────────────

    private static ResultRecord R(int swimmerId) => new()
    {
        SwimmerId = swimmerId, CompetitionId = 1, ClubId = 1, StyleId = 1,
        CompetitionDate = new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc),
        Distance = "100", Gender = "F",
    };

    private static async Task SeedStyleAsync(SwimmDbContext db)
    {
        db.Styles.Add(new Style { Id = 1, Name = "freestyle" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task RunBatch_LinksAutoVerify_SkipsNonHebrewAndResultless()
    {
        await using var db = CreateDb(nameof(RunBatch_LinksAutoVerify_SkipsNonHebrewAndResultless));
        await SeedStyleAsync(db);
        var hebrew = S("ברנצב", "סבינה", 2017);
        var latin = S("Smith", "John", 1990);      // латиница — квоту не жжём
        var noResults = S("כהן", "נועה", 2015);    // без результатов — сверке не с чем работать
        db.Swimmers.AddRange(hebrew, latin, noResults);
        await db.SaveChangesAsync();
        db.Results.AddRange(R(hebrew.Id), R(latin.Id));
        await db.SaveChangesAsync();

        var service = Service(db,
            search: new FakeSearchProvider([111]),
            client: new FakeLogligClient(new Dictionary<int, LogligPlayerCard?> { [111] = Card("סבינה ברנצב") }),
            match: new FakeMatchService(_ => LogligMatchDecision.AutoVerify));

        var report = await service.RunBatchAsync(10, CancellationToken.None);

        Assert.Equal(new LogligBatchReport(1, 1, 0, 0), report);
        Assert.Equal(111, (await db.Swimmers.SingleAsync(s => s.Id == hebrew.Id)).LogligId);
        Assert.Null((await db.Swimmers.SingleAsync(s => s.Id == latin.Id)).LogligId);
        Assert.Null((await db.Swimmers.SingleAsync(s => s.Id == noResults.Id)).LogligId);
    }

    [Fact]
    public async Task RunBatch_RespectsTake_AndCountsOutcomes()
    {
        await using var db = CreateDb(nameof(RunBatch_RespectsTake_AndCountsOutcomes));
        await SeedStyleAsync(db);
        var s1 = S("ברנצב", "סבינה", 2017);
        var s2 = S("כהן", "נועה", 2015);
        var s3 = S("לוי", "דן", 2016);
        db.Swimmers.AddRange(s1, s2, s3);
        await db.SaveChangesAsync();
        // s1 — 3 результата, s2 — 2, s3 — 1: порядок прогона по числу результатов.
        db.Results.AddRange(R(s1.Id), R(s1.Id), R(s1.Id), R(s2.Id), R(s2.Id), R(s3.Id));
        await db.SaveChangesAsync();

        // Поиск ничего не находит → оба обработанных уходят в NothingFound; s3 не трогаем (take=2).
        var service = Service(db, search: new FakeSearchProvider([]));

        var report = await service.RunBatchAsync(2, CancellationToken.None);

        Assert.Equal(new LogligBatchReport(2, 0, 0, 2), report);
    }

    [Fact]
    public async Task RunBatch_SearchNotConfigured_DoesNothing()
    {
        await using var db = CreateDb(nameof(RunBatch_SearchNotConfigured_DoesNothing));
        await SeedStyleAsync(db);
        var s = S("ברנצב", "סבינה", 2017);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();
        db.Results.Add(R(s.Id));
        await db.SaveChangesAsync();

        var service = Service(db, search: new FakeSearchProvider(null, configured: false));

        Assert.Equal(new LogligBatchReport(0, 0, 0, 0), await service.RunBatchAsync(10, CancellationToken.None));
    }
}
