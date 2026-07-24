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
/// Краудсорс-привязка Loglig ID (docs/loglig-id-plan.md, шаг 6): гарды предложения и ночная
/// верификация (имя + год; спорные — полной сверкой). Фейки интерфейсов — стабы (Moq нет).
/// </summary>
public class LogligSuggestionServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static Swimmer S(string last, string first, int year,
        string? status = null, int? logligId = null) =>
        new()
        {
            LastName = last, FirstName = first, BirthYear = year,
            LogligId = logligId, LogligIdStatus = status,
            LogligIdSource = status != null ? "user-claim" : null,
        };

    private sealed class FakeLogligClient(IReadOnlyDictionary<int, LogligPlayerCard?> cards) : ILogligClient
    {
        public Task<LogligPlayerCard?> GetPlayerCardAsync(int logligId, CancellationToken ct = default)
            => Task.FromResult(cards.TryGetValue(logligId, out var card) ? card : null);

        public string BuildPublicProfileUrl(int logligId)
            => $"https://loglig.com:2053/Players/Details/{logligId}?seasonId=1715";
    }

    private sealed class FakeMatchService(LogligMatchDecision decision) : ILogligMatchService
    {
        public LogligMatchReport Match(LogligPlayerCard card, int swimmerBirthYear, string? swimmerClubName, IReadOnlyList<LocalResultKey> localResults)
            => new(decision, false, false, 0, []);
    }

    private static LogligPlayerCard Card(string name, int? year) => new(name, year, "F", "Club", []);

    private static LogligSuggestionService Service(
        SwimmDbContext db,
        ILogligClient? client = null,
        LogligMatchDecision fallbackDecision = LogligMatchDecision.NoMatch) =>
        new(db,
            client ?? new FakeLogligClient(new Dictionary<int, LogligPlayerCard?>()),
            new FakeMatchService(fallbackDecision),
            NullLogger<LogligSuggestionService>.Instance);

    // ── SuggestAsync: гарды ───────────────────────────────────────────────────

    [Fact]
    public async Task Suggest_Unlinked_SavesSuggested()
    {
        await using var db = CreateDb(nameof(Suggest_Unlinked_SavesSuggested));
        var s = S("ברנצב", "סבינה", 2017);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        var result = await Service(db).SuggestAsync(s.Id, 304199, userId: 42);

        Assert.True(result.Accepted);
        var saved = await db.Swimmers.SingleAsync();
        Assert.Equal(304199, saved.LogligId);
        Assert.Equal("Suggested", saved.LogligIdStatus);
        Assert.Equal("user-claim", saved.LogligIdSource);
        Assert.Equal(42, saved.LogligIdSuggestedByUserId);
        Assert.NotNull(saved.LogligIdSuggestedAt);
        Assert.Null(saved.LogligIdVerifiedAt);
    }

    [Theory]
    [InlineData("Verified")]
    [InlineData("Suggested")]
    public async Task Suggest_VerifiedOrPending_Rejected(string status)
    {
        await using var db = CreateDb(nameof(Suggest_VerifiedOrPending_Rejected) + status);
        var s = S("ברנצב", "סבינה", 2017, status, 111);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        var result = await Service(db).SuggestAsync(s.Id, 222, userId: 1);

        Assert.False(result.Accepted);
        Assert.NotNull(result.Error);
        Assert.Equal(111, (await db.Swimmers.SingleAsync()).LogligId);
    }

    [Fact]
    public async Task Suggest_SameRejectedId_Refused_OtherIdAccepted()
    {
        await using var db = CreateDb(nameof(Suggest_SameRejectedId_Refused_OtherIdAccepted));
        var s = S("ברנצב", "סבינה", 2017, "Rejected", 111);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        var same = await Service(db).SuggestAsync(s.Id, 111, userId: 1);
        Assert.False(same.Accepted);

        var other = await Service(db).SuggestAsync(s.Id, 222, userId: 1);
        Assert.True(other.Accepted);
        Assert.Equal("Suggested", (await db.Swimmers.SingleAsync()).LogligIdStatus);
    }

    [Fact]
    public async Task Suggest_IdHeldByVerifiedSwimmer_Refused()
    {
        await using var db = CreateDb(nameof(Suggest_IdHeldByVerifiedSwimmer_Refused));
        var holder = S("אחר", "מישהו", 2010, "Verified", 111);
        var s = S("ברנצב", "סבינה", 2017);
        db.Swimmers.AddRange(holder, s);
        await db.SaveChangesAsync();

        var result = await Service(db).SuggestAsync(s.Id, 111, userId: 1);

        Assert.False(result.Accepted);
        Assert.Null((await db.Swimmers.SingleAsync(x => x.Id == s.Id)).LogligId);
    }

    [Fact]
    public async Task Suggest_IdHeldByRejectedSwimmer_FreesHolderAndAccepts()
    {
        await using var db = CreateDb(nameof(Suggest_IdHeldByRejectedSwimmer_FreesHolderAndAccepts));
        var holder = S("אחר", "מישהו", 2010, "Rejected", 111);
        var s = S("ברנצב", "סבינה", 2017);
        db.Swimmers.AddRange(holder, s);
        await db.SaveChangesAsync();

        var result = await Service(db).SuggestAsync(s.Id, 111, userId: 1);

        Assert.True(result.Accepted);
        var freedHolder = await db.Swimmers.SingleAsync(x => x.Id == holder.Id);
        Assert.Null(freedHolder.LogligId);
        Assert.Null(freedHolder.LogligIdStatus);
        Assert.Equal(111, (await db.Swimmers.SingleAsync(x => x.Id == s.Id)).LogligId);
    }

    // ── GetStatusAsync: публичный статус ──────────────────────────────────────

    [Fact]
    public async Task GetStatus_ExistingSwimmer_ReturnsStatus()
    {
        await using var db = CreateDb(nameof(GetStatus_ExistingSwimmer_ReturnsStatus));
        var s = S("ברנצב", "סבינה", 2017, "Verified", 304199);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        var result = await Service(db).GetStatusAsync(s.Id);

        Assert.Equal("Verified", result.Status);
        Assert.Equal("https://loglig.com:2053/Players/Details/304199?seasonId=1715", result.ProfileUrl);
    }

    [Fact]
    public async Task GetStatus_SuggestedSwimmer_HidesProfileUrl()
    {
        await using var db = CreateDb(nameof(GetStatus_SuggestedSwimmer_HidesProfileUrl));
        var s = S("ברנצב", "סבינה", 2017, "Suggested", 304199);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        var result = await Service(db).GetStatusAsync(s.Id);

        Assert.Equal("Suggested", result.Status);
        Assert.Null(result.ProfileUrl);
    }

    [Fact]
    public async Task GetStatus_UnknownSwimmer_ReturnsNull()
    {
        await using var db = CreateDb(nameof(GetStatus_UnknownSwimmer_ReturnsNull));

        var result = await Service(db).GetStatusAsync(999999);

        Assert.Null(result.Status);
        Assert.Null(result.ProfileUrl);
    }

    // ── VerifySuggestedAsync: ночной джоб ─────────────────────────────────────

    [Fact]
    public async Task Verify_NameAndYearMatch_BothOrders_Verified()
    {
        await using var db = CreateDb(nameof(Verify_NameAndYearMatch_BothOrders_Verified));
        var s1 = S("ברנצב", "סבינה", 2017, "Suggested", 111);
        var s2 = S("כהן", "נועה", 2015, "Suggested", 222);
        db.Swimmers.AddRange(s1, s2);
        await db.SaveChangesAsync();

        var client = new FakeLogligClient(new Dictionary<int, LogligPlayerCard?>
        {
            [111] = Card("סבינה ברנצב", 2017),   // имя-фамилия
            [222] = Card("כהן נועה", 2015),      // фамилия-имя
        });
        var report = await Service(db, client).VerifySuggestedAsync();

        Assert.Equal(new LogligSuggestionVerifyReport(2, 2, 0, 0), report);
        Assert.All(await db.Swimmers.ToListAsync(), x =>
        {
            Assert.Equal("Verified", x.LogligIdStatus);
            Assert.NotNull(x.LogligIdVerifiedAt);
        });
    }

    [Fact]
    public async Task Verify_YearMismatch_NoResultMatch_Rejected_KeepsId()
    {
        await using var db = CreateDb(nameof(Verify_YearMismatch_NoResultMatch_Rejected_KeepsId));
        var s = S("ברנצב", "סבינה", 2017, "Suggested", 111);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        var client = new FakeLogligClient(new Dictionary<int, LogligPlayerCard?>
        {
            [111] = Card("סבינה ברנצב", 1999),
        });
        var report = await Service(db, client, LogligMatchDecision.NoMatch).VerifySuggestedAsync();

        Assert.Equal(new LogligSuggestionVerifyReport(1, 0, 1, 0), report);
        var saved = await db.Swimmers.SingleAsync();
        Assert.Equal("Rejected", saved.LogligIdStatus);
        Assert.Equal(111, saved.LogligId); // анти-спам: отклонённый ID сохраняется
    }

    [Fact]
    public async Task Verify_NameMismatch_ButResultsAutoVerify_Verified()
    {
        await using var db = CreateDb(nameof(Verify_NameMismatch_ButResultsAutoVerify_Verified));
        var s = S("ברנצב", "סבינה", 2017, "Suggested", 111);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        // Имя в карточке разночтётся (уменьшительное), но полная сверка результатов подтверждает.
        var client = new FakeLogligClient(new Dictionary<int, LogligPlayerCard?>
        {
            [111] = Card("סבי ברנצב", 2017),
        });
        var report = await Service(db, client, LogligMatchDecision.AutoVerify).VerifySuggestedAsync();

        Assert.Equal(new LogligSuggestionVerifyReport(1, 1, 0, 0), report);
        Assert.Equal("Verified", (await db.Swimmers.SingleAsync()).LogligIdStatus);
    }

    [Fact]
    public async Task Verify_CardUnavailable_SkippedStaysSuggested()
    {
        await using var db = CreateDb(nameof(Verify_CardUnavailable_SkippedStaysSuggested));
        var s = S("ברנצב", "סבינה", 2017, "Suggested", 111);
        db.Swimmers.Add(s);
        await db.SaveChangesAsync();

        var report = await Service(db).VerifySuggestedAsync(); // фейк-клиент без карточек

        Assert.Equal(new LogligSuggestionVerifyReport(1, 0, 0, 1), report);
        Assert.Equal("Suggested", (await db.Swimmers.SingleAsync()).LogligIdStatus);
    }

    // ── IsNameAndYearMatch: нормализация ──────────────────────────────────────

    [Theory]
    [InlineData("סבינה ברנצב", 2017, true)]
    [InlineData("ברנצב סבינה", 2017, true)]
    [InlineData("  סבינה   ברנצב ", 2017, true)] // лишние пробелы схлопываются
    [InlineData("סבינה ברנצב", 2016, false)]     // год не совпал
    [InlineData("נועה כהן", 2017, false)]        // имя не совпало
    public void IsNameAndYearMatch_Cases(string cardName, int cardYear, bool expected)
    {
        var swimmer = new Swimmer { LastName = "ברנצב", FirstName = "סבינה", BirthYear = 2017 };
        Assert.Equal(expected, LogligSuggestionService.IsNameAndYearMatch(Card(cardName, cardYear), swimmer));
    }

    [Fact]
    public void IsNameAndYearMatch_CardWithoutYear_False()
    {
        var swimmer = new Swimmer { LastName = "ברנצב", FirstName = "סבינה", BirthYear = 2017 };
        Assert.False(LogligSuggestionService.IsNameAndYearMatch(Card("סבינה ברנצב", null), swimmer));
    }
}
