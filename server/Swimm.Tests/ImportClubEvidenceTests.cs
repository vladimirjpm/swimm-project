using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Ф4 (Р110, §7.2 docs/plans/swimmer-club-change-plan.md): «тот же клуб» на запасных путях узнавания
/// пловца — клуб карточки, ИЛИ любой клуб за 2 последних сезона, ИЛИ клуб на карточке loglig
/// (<see cref="ImportClubEvidence"/>). Проверяется через <see cref="ImportSwimmerResolver"/> — все три
/// пути (другое написание, переставленные половины, год со сдвигом на сто лет) и нога эстафеты.
/// Сеть — подставной <see cref="ILogligClient"/>.
/// </summary>
public class ImportClubEvidenceTests
{
    private const int Dolphin = 438, Raanana = 449, Shoham = 500;

    private static SwimmDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    internal sealed class FakeLoglig(Dictionary<int, string?> clubs) : ILogligClient
    {
        public List<int> Calls { get; } = [];

        public Task<LogligPlayerCard?> GetPlayerCardAsync(int logligId, int? seasonId = null, CancellationToken ct = default)
        {
            Calls.Add(logligId);
            return Task.FromResult(clubs.TryGetValue(logligId, out var club)
                ? new LogligPlayerCard("X", 2015, "F", club, [])
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

    private static ResultJsonItem Row(string last, string first, int year = 2015) =>
        new() { LastName = last, FirstName = first, BirthYear = year };

    private static RelaySwimmerJson Leg(string last, string first, int year = 2015) =>
        new() { LastName = last, FirstName = first, BirthYear = year, Order = 1 };

    /// <summary>Три клуба; карточка «יוקליס עמליה» (2015) в Дельфине — как до перехода.</summary>
    private static async Task<(SwimmDbContext Db, Club Raanana, Swimmer Card)> SeedAsync(
        int? logligId = null, string logligStatus = "Verified")
    {
        var db = CreateDb();
        db.Clubs.AddRange(
            new Club { Id = Dolphin, Name = "הפועל דולפין נתניה" },
            new Club { Id = Raanana, Name = "ע.ל.ה עצמה רעננה" },
            new Club { Id = Shoham, Name = "מכבי שוהם" });
        var card = new Swimmer
        {
            Id = 61745, LastName = "יוקליס", FirstName = "עמליה", BirthYear = 2015, ClubId = Dolphin,
            LogligId = logligId, LogligIdStatus = logligId is null ? null : logligStatus,
        };
        db.Swimmers.Add(card);
        await db.SaveChangesAsync();
        return (db, await db.Clubs.SingleAsync(c => c.Id == Raanana), card);
    }

    private static async Task AddHistoryAsync(SwimmDbContext db, int swimmerId, int clubId, DateTime date)
    {
        db.Results.Add(new ResultRecord
        {
            SwimmerId = swimmerId, ClubId = clubId, CompetitionId = 1, StyleId = 1,
            CompetitionDate = date, Distance = "50", Gender = "female",
        });
        await db.SaveChangesAsync();
    }

    private static DateTime ThisSeason => SeasonMath.StartOf(SeasonMath.CurrentStartYear()).AddDays(20);

    [Fact]
    public async Task Spelling_CardClub_AsBefore()
    {
        var (db, _, card) = await SeedAsync();
        var dolphin = await db.Clubs.SingleAsync(c => c.Id == Dolphin);
        var log = new List<string>();
        var resolver = await ImportSwimmerResolver.LoadAsync(db, log);

        var (found, _) = await resolver.ResolveAsync(Row("יוקלס", "עמליה"), dolphin, 1);

        Assert.Same(card, found);
        Assert.Contains(log, l => l.StartsWith("Другое написание:") && l.Contains("клуб карточки"));
    }

    [Fact]
    public async Task Spelling_ClubFromHistory_RecognisedWithoutLoglig()
    {
        // Уже плыла за Раанану в этом сезоне, карточка ещё на Дельфине (кандидат ждёт loglig).
        var (db, raanana, card) = await SeedAsync();
        await AddHistoryAsync(db, card.Id, Raanana, ThisSeason);
        var log = new List<string>();
        var loglig = new FakeLoglig([]);
        var resolver = await ImportSwimmerResolver.LoadAsync(db, log, loglig, TimeSpan.Zero);

        var (found, _) = await resolver.ResolveAsync(Row("יוקלס", "עמליה"), raanana, 1);

        Assert.Same(card, found);
        Assert.Equal(1, await db.Swimmers.CountAsync());
        Assert.Empty(loglig.Calls);   // история нашлась — в сеть не ходили
        Assert.Contains(log, l => l.Contains("клуб из истории за 2 сезона"));
    }

    [Fact]
    public async Task Spelling_HistoryOlderThanTwoSeasons_NotEvidence()
    {
        var (db, raanana, card) = await SeedAsync();
        await AddHistoryAsync(db, card.Id, Raanana, SeasonMath.StartOf(SeasonMath.CurrentStartYear() - 2).AddDays(20));
        var resolver = await ImportSwimmerResolver.LoadAsync(db, []);

        var (found, _) = await resolver.ResolveAsync(Row("יוקלס", "עמליה"), raanana, 1);

        Assert.NotSame(card, found);
        Assert.Equal(2, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task Transferred_DifferentSpelling_RecognisedViaLoglig_NoDuplicate()
    {
        // Главный случай Ф4: первый старт за новый клуб, другое написание, карточка ещё на Дельфине,
        // истории за Раанану нет — loglig показывает Раанану → тот же человек, дубля нет.
        var (db, raanana, card) = await SeedAsync(logligId: 112233);
        var log = new List<string>();
        var loglig = new FakeLoglig(new() { [112233] = "ע.ל.ה עצמה רעננה" });
        var resolver = await ImportSwimmerResolver.LoadAsync(db, log, loglig, TimeSpan.Zero);

        var (found, _) = await resolver.ResolveAsync(Row("יוקלס", "עמליה"), raanana, 1);

        Assert.Same(card, found);
        Assert.Equal(1, await db.Swimmers.CountAsync());
        Assert.Equal([112233], loglig.Calls);
        Assert.Contains(log, l => l.Contains("клуб на карточке loglig #112233"));
    }

    [Fact]
    public async Task Loglig_AnswerInSharedStore_NoNetwork()
    {
        // Общее место ответов loglig (Р112): его могла записать сверка кандидатов (Р98) или кнопка
        // «Проверить в loglig» на /Admin/Clubs — импорт второй раз за сутки в сеть не идёт.
        var (db, raanana, card) = await SeedAsync(logligId: 112233);
        db.SwimmerLogligClubs.Add(new SwimmerLogligClub
        {
            SwimmerId = card.Id, LogligId = 112233, ClubName = "ע.ל.ה עצמה רעננה", CheckedAt = DateTime.UtcNow.AddHours(-2),
        });
        await db.SaveChangesAsync();
        var loglig = new FakeLoglig([]);
        var resolver = await ImportSwimmerResolver.LoadAsync(db, [], loglig, TimeSpan.Zero);

        var (found, _) = await resolver.ResolveAsync(Row("יוקלס", "עמליה"), raanana, 1);

        Assert.Same(card, found);
        Assert.Empty(loglig.Calls);
    }

    [Theory]
    [InlineData(112233, -25)]   // ответу больше суток
    [InlineData(999999, -2)]    // свежий, но про другой loglig-id
    public async Task Loglig_StaleOrForeignStoredAnswer_GoesToNetwork(int storedLogligId, int hoursAgo)
    {
        var (db, raanana, card) = await SeedAsync(logligId: 112233);
        db.SwimmerLogligClubs.Add(new SwimmerLogligClub
        {
            SwimmerId = card.Id, LogligId = storedLogligId, ClubName = "הפועל דולפין נתניה",
            CheckedAt = DateTime.UtcNow.AddHours(hoursAgo),
        });
        await db.SaveChangesAsync();
        var loglig = new FakeLoglig(new() { [112233] = "ע.ל.ה עצמה רעננה" });
        var resolver = await ImportSwimmerResolver.LoadAsync(db, [], loglig, TimeSpan.Zero);

        var (found, _) = await resolver.ResolveAsync(Row("יוקלס", "עמליה"), raanana, 1);

        Assert.Same(card, found);
        Assert.Equal([112233], loglig.Calls);
    }

    [Fact]
    public async Task Loglig_NetworkAnswer_SavedToSharedStore()
    {
        // Ответ сети — в общее место: дашборд и /Admin/Clubs возьмут его, не открывая карточку снова.
        var (db, raanana, card) = await SeedAsync(logligId: 112233);
        var loglig = new FakeLoglig(new() { [112233] = " ע.ל.ה עצמה רעננה " });
        var resolver = await ImportSwimmerResolver.LoadAsync(db, [], loglig, TimeSpan.Zero);

        await resolver.ResolveAsync(Row("יוקלס", "עמליה"), raanana, 1);
        await db.SaveChangesAsync();   // сохраняет импорт — как и остальное, что он нашёл

        var row = await db.SwimmerLogligClubs.AsNoTracking().SingleAsync();
        Assert.Equal(card.Id, row.SwimmerId);
        Assert.Equal(112233, row.LogligId);
        Assert.Equal("ע.ל.ה עצמה רעננה", row.ClubName);
        Assert.True(row.CheckedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Loglig_CardNotOpened_NothingSaved()
    {
        // Карточка не открылась — ответа нет, в общее место ничего не пишем (как у сверки кандидатов).
        var (db, raanana, _) = await SeedAsync(logligId: 112233);
        var resolver = await ImportSwimmerResolver.LoadAsync(db, [], new FakeLoglig([]), TimeSpan.Zero);

        await resolver.ResolveAsync(Row("יוקלס", "עמליה"), raanana, 1);
        await db.SaveChangesAsync();

        Assert.Empty(await db.SwimmerLogligClubs.ToListAsync());
    }

    [Theory]
    [InlineData(null, "Verified")]       // нет loglig-id
    [InlineData(112233, "Suggested")]    // предложен, не подтверждён — карточка может быть чужой
    public async Task Loglig_WithoutVerifiedId_NoNetwork_NewCard(int? logligId, string status)
    {
        var (db, raanana, card) = await SeedAsync(logligId, status);
        var loglig = new FakeLoglig(new() { [112233] = "ע.ל.ה עצמה רעננה" });
        var resolver = await ImportSwimmerResolver.LoadAsync(db, [], loglig, TimeSpan.Zero);

        var (found, _) = await resolver.ResolveAsync(Row("יוקלס", "עמליה"), raanana, 1);

        Assert.NotSame(card, found);
        Assert.Empty(loglig.Calls);
    }

    [Theory]
    [InlineData("ארכיון איגוד")]      // снят с регистрации — не доказательство
    [InlineData("מכבי שוהם")]          // третий клуб
    [InlineData(null)]                  // карточка не открылась
    public async Task Loglig_OtherAnswer_NewCard(string? logligClub)
    {
        var (db, raanana, card) = await SeedAsync(logligId: 112233);
        var loglig = new FakeLoglig(logligClub is null ? [] : new() { [112233] = logligClub });
        var resolver = await ImportSwimmerResolver.LoadAsync(db, [], loglig, TimeSpan.Zero);

        var (found, _) = await resolver.ResolveAsync(Row("יוקלס", "עמליה"), raanana, 1);

        Assert.NotSame(card, found);
        Assert.Equal(2, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task Loglig_OneRequestPerSwimmerPerImport()
    {
        var (db, raanana, card) = await SeedAsync(logligId: 112233);
        var loglig = new FakeLoglig(new() { [112233] = "מכבי שוהם" });
        var resolver = await ImportSwimmerResolver.LoadAsync(db, [], loglig, TimeSpan.Zero);

        await resolver.ResolveAsync(Row("יוקלס", "עמליה"), raanana, 1);
        await resolver.ResolveAsync(Row("יוקלייס", "עמליה"), raanana, 2);   // ещё одно написание — тот же кандидат, ответ из кэша импорта

        Assert.Single(loglig.Calls);
    }

    [Fact]
    public async Task History_TwoCandidates_Ambiguous_NewCard()
    {
        // Двое с тем же скелетом имени и годом плыли за Раанану — гадать нельзя.
        var (db, raanana, card) = await SeedAsync();
        var twin = new Swimmer { Id = 61746, LastName = "יוקלייס", FirstName = "עמליה", BirthYear = 2015, ClubId = Shoham };
        db.Swimmers.Add(twin);
        await db.SaveChangesAsync();
        await AddHistoryAsync(db, card.Id, Raanana, ThisSeason);
        await AddHistoryAsync(db, twin.Id, Raanana, ThisSeason);
        var resolver = await ImportSwimmerResolver.LoadAsync(db, []);

        var (found, _) = await resolver.ResolveAsync(Row("יוקלס", "עמליה"), raanana, 1);

        Assert.NotSame(card, found);
        Assert.NotSame(twin, found);
    }

    [Fact]
    public async Task History_MergedClub_CountsAsItsCanon()
    {
        // История — за клуб, позже склеенный в Раанану: канон тот же.
        var (db, raanana, card) = await SeedAsync();
        db.Clubs.Add(new Club { Id = 777, Name = "עצמה רעננה ישן", MergedIntoId = Raanana });
        await db.SaveChangesAsync();
        await AddHistoryAsync(db, card.Id, 777, ThisSeason);
        var resolver = await ImportSwimmerResolver.LoadAsync(db, []);

        var (found, _) = await resolver.ResolveAsync(Row("יוקלס", "עמליה"), raanana, 1);

        Assert.Same(card, found);
    }

    [Fact]
    public async Task SwappedHalves_ClubFromHistory()
    {
        // Р83 + Р110: «מזרחי | גבריאל שלמה» в карточке, протокол переставил половины, клуб новый.
        var (db, raanana, _) = await SeedAsync();
        var card = new Swimmer { Id = 59052, LastName = "מזרחי", FirstName = "גבריאל שלמה", BirthYear = 1975, ClubId = Dolphin };
        db.Swimmers.Add(card);
        await db.SaveChangesAsync();
        await AddHistoryAsync(db, card.Id, Raanana, ThisSeason);
        var log = new List<string>();
        var resolver = await ImportSwimmerResolver.LoadAsync(db, log);

        var (found, _) = await resolver.ResolveAsync(Row("גבריאל שלמה", "מזרחי", 1975), raanana, 1);

        Assert.Same(card, found);
        Assert.Contains(log, l => l.StartsWith("Переставленные имя и фамилия:") && l.Contains("клуб из истории"));
    }

    [Fact]
    public async Task CenturyShift_ClubFromLoglig()
    {
        // Р104 + Р110: год 2046 вместо 1946, а пловчиха уже в другом клубе — loglig подтверждает.
        var (db, raanana, _) = await SeedAsync();
        var card = new Swimmer
        {
            Id = 64000, LastName = "ברנשטיין", FirstName = "לאה", BirthYear = 1946, ClubId = Dolphin,
            LogligId = 112959, LogligIdStatus = "Verified",
        };
        db.Swimmers.Add(card);
        await db.SaveChangesAsync();
        var log = new List<string>();
        var loglig = new FakeLoglig(new() { [112959] = "ע.ל.ה עצמה רעננה" });
        var resolver = await ImportSwimmerResolver.LoadAsync(db, log, loglig, TimeSpan.Zero);

        var (found, _) = await resolver.ResolveAsync(Row("ברנשטיין", "לאה", 2046), raanana, 1);

        Assert.Same(card, found);
        Assert.Contains(log, l => l.StartsWith("Год со сдвигом на сто лет:") && l.Contains("loglig"));
    }

    [Fact]
    public async Task CenturyShift_OtherClubWithoutEvidence_NewCard()
    {
        var (db, raanana, _) = await SeedAsync();
        db.Swimmers.Add(new Swimmer { Id = 64000, LastName = "ברנשטיין", FirstName = "לאה", BirthYear = 1946, ClubId = Dolphin });
        await db.SaveChangesAsync();
        var resolver = await ImportSwimmerResolver.LoadAsync(db, []);

        var (found, _) = await resolver.ResolveAsync(Row("ברנשטיין", "לאה", 2046), raanana, 1);

        Assert.NotEqual(64000, found.Id);
    }

    [Fact]
    public async Task RelayLeg_DifferentSpelling_ClubFromHistory()
    {
        var (db, raanana, card) = await SeedAsync();
        await AddHistoryAsync(db, card.Id, Raanana, ThisSeason);
        var resolver = await ImportSwimmerResolver.LoadAsync(db, []);

        var members = await resolver.ResolveRelayMembersAsync([Leg("יוקלס", "עמליה")], raanana);

        Assert.Equal(card.Id, Assert.Single(members).SwimmerId);
        Assert.Equal(1, await db.Swimmers.CountAsync());
    }
}
