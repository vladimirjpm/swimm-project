using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Swimm.Infrastructure.Services.DataChecks;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Английские имена, не подходящие к ивритским (10.10.2026, docs/plans/en-names-handoff.md):
/// решение по карточке loglig (<see cref="EnglishNameRepairPlanner"/>), починка с аудитом
/// (<see cref="EnglishNameRepairService"/>) и проверка реестра (<see cref="SwimmerEnglishNameCheck"/>).
/// Случаи — настоящие пловцы comp 1486 и находки калибровки по всей базе.
/// </summary>
public class EnglishNameRepairTests
{
    // ── Решение ──────────────────────────────────────────────────────────────

    [Fact]
    public void Planner_CardFits_Writes()
    {
        var d = EnglishNameRepairPlanner.Decide("סגרון", "נועם", "סגרון", "EYTAN", "SIGRON NOAM");
        Assert.Equal((EnglishNameRepairAction.Write, "SIGRON", "NOAM"), (d.Action, d.LastEn, d.FirstEn));
    }

    [Fact]
    public void Planner_SecondNameOnlyInLatin_Writes()
    {
        var d = EnglishNameRepairPlanner.Decide("ברוטמן", "מיכאל", "ברוטמן", "BRUTMAN", "MICHEL LEIB BRUTMAN");
        Assert.Equal((EnglishNameRepairAction.Write, "BRUTMAN", "MICHEL LEIB"), (d.Action, d.LastEn, d.FirstEn));
    }

    [Fact]
    public void Planner_CardSpellsTheSame_Keeps()
    {
        // Прозвище: loglig пишет так же — не ошибка импорта, находку закрывают «Принять».
        var d = EnglishNameRepairPlanner.Decide("מרדכי", "עיון", "AYUN", "MOTI", "MOTI AYUN");
        Assert.Equal(EnglishNameRepairAction.Keep, d.Action);
    }

    [Fact]
    public void Planner_NeighboursName_CardAlsoForeign_Clears()
    {
        // «ELKIN DANIEL» — имя соседа; карточка «ADAM FOGEL» с другим именем тоже не подходит.
        var d = EnglishNameRepairPlanner.Decide("פוגל דודוביץ", "איתן", "ELKIN", "DANIEL", "ADAM FOGEL");
        Assert.Equal((EnglishNameRepairAction.Clear, "", ""), (d.Action, d.LastEn, d.FirstEn));
    }

    [Fact]
    public void Planner_HebrewCard_IsNoCard()
    {
        // Латиницы у игрока нет — loglig отдаёт ивритский заголовок; «пишет так же» тут не довод.
        var d = EnglishNameRepairPlanner.Decide("גלסמן", "יסמין", "גלסמן", "יסמין", "יסמין גלסמן");
        Assert.Equal(EnglishNameRepairAction.Clear, d.Action);
    }

    [Fact]
    public void Planner_ShortFirstNameAndSecondName_Writes()
    {
        // «ELLA» — одна согласная, но совпала с «אלה»; «KLARA» — второе имя только в латинице.
        var d = EnglishNameRepairPlanner.Decide("בלמס", "אלה", "ELLAKLARA", "BELAMS", "ELLA KLARA BELAMS");
        Assert.Equal((EnglishNameRepairAction.Write, "BELAMS", "ELLA KLARA"), (d.Action, d.LastEn, d.FirstEn));
    }

    [Fact]
    public void Planner_HebrewBoundaryWrong_DoesNotFitCardIntoIt()
    {
        // «עבד | אלקדר» — граница неверна уже в иврите; «ABED ALRAHMAN ABED | ALKADER» не пишем.
        var d = EnglishNameRepairPlanner.Decide("עבד", "אלקדר", "עבד", "אלקדר", "ABED ALRAHMAN ABED ALKADER");
        Assert.Equal(EnglishNameRepairAction.Clear, d.Action);
    }

    [Fact]
    public void Planner_HebrewInLatin_NoCard_Clears() =>
        Assert.Equal(EnglishNameRepairAction.Clear,
            EnglishNameRepairPlanner.Decide("ברוטמן", "מיכאל", "ברוטמן", "BRUTMAN", null).Action);

    [Fact]
    public void Planner_Foreign_NoCard_Keeps() =>
        Assert.Equal(EnglishNameRepairAction.Keep,
            EnglishNameRepairPlanner.Decide("פוגל דודוביץ", "איתן", "ELKIN", "DANIEL", null).Action);

    [Fact]
    public void Planner_AlreadyFits_Keeps() =>
        Assert.Equal(EnglishNameRepairAction.Keep,
            EnglishNameRepairPlanner.Decide("ווייס", "עמיחי", "WEISS", "AMICHAI", "AMICHAI WEISS").Action);

    // ── Сервис ───────────────────────────────────────────────────────────────

    private static DbContextOptions<SwimmDbContext> Options(string name) =>
        new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static async Task<(SwimmDbContext Db, string Name)> SeedAsync()
    {
        var name = Guid.NewGuid().ToString();
        var db = new SwimmDbContext(Options(name));
        db.Swimmers.AddRange(
            new Swimmer { Id = 1, LastName = "סגרון", FirstName = "נועם", BirthYear = 2016, LastNameEn = "סגרון", FirstNameEn = "EYTAN", LogligId = 303531 },
            new Swimmer { Id = 2, LastName = "פוגל דודוביץ", FirstName = "איתן", BirthYear = 2016, LastNameEn = "ELKIN", FirstNameEn = "DANIEL", LogligId = 316166 },
            new Swimmer { Id = 3, LastName = "ווייס", FirstName = "עמיחי", BirthYear = 2016, LastNameEn = "WEISS", FirstNameEn = "AMICHAI", LogligId = 316100 });
        await db.SaveChangesAsync();
        return (db, name);
    }

    private static EnglishNameRepairService Service(SwimmDbContext db, ILogligClient loglig)
    {
        EnglishNameRepairService.PoliteDelay = TimeSpan.Zero;
        return new EnglishNameRepairService(db, loglig, NullLogger<EnglishNameRepairService>.Instance);
    }

    private static readonly Dictionary<int, string> Cards = new()
    {
        [303531] = "SIGRON NOAM", [316166] = "ADAM FOGEL", [316100] = "AMICHAI WEISS",
    };

    [Fact]
    public async Task DryRun_DecidesButWritesNothing()
    {
        var (db, _) = await SeedAsync();
        var report = await Service(db, new FakeLoglig(Cards)).RepairAsync(apply: false);

        Assert.False(report.Applied);
        Assert.Equal([1, 2], report.Rows.Select(r => r.SwimmerId));   // #3 подходит — не кандидат
        Assert.Equal((1, 1, 0), (report.Written, report.Cleared, report.Kept));
        Assert.Equal("סגרון", (await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 1)).LastNameEn);
        Assert.Empty(db.AdminAudits);
    }

    [Fact]
    public async Task Apply_WritesWithAudit()
    {
        var (db, _) = await SeedAsync();
        var report = await Service(db, new FakeLoglig(Cards)).RepairAsync(apply: true);

        Assert.Equal(0, report.Skipped);
        var s1 = await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 1);
        var s2 = await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 2);
        Assert.Equal(("SIGRON", "NOAM"), (s1.LastNameEn, s1.FirstNameEn));
        Assert.Equal(("", ""), (s2.LastNameEn, s2.FirstNameEn));

        var audit = await db.AdminAudits.Where(a => a.Action == "swimmer.en-name-fix").OrderBy(a => a.EntityId).ToListAsync();
        Assert.Equal(["1", "2"], audit.Select(a => a.EntityId));
        Assert.Contains("\"loglig\":\"SIGRON NOAM\"", audit[0].DetailsJson);
        Assert.Contains("סגרון", audit[0].DetailsJson);   // иврит читается, а не ס…
    }

    [Fact]
    public async Task Apply_SkipsSwimmerChangedAfterRead()
    {
        // Пока читались карточки, кто-то поправил пловца #1 руками — его правку не перетираем.
        var (db, name) = await SeedAsync();
        var loglig = new FakeLoglig(Cards, onCard: id =>
        {
            if (id != 303531) return;
            using var other = new SwimmDbContext(Options(name));
            other.Swimmers.Single(s => s.Id == 1).LastNameEn = "SIGRON-MANUAL";
            other.SaveChanges();
        });

        // Свой контекст, как у CLI: контекст посева держит пловцов в трекере со старыми полями.
        using var fresh = new SwimmDbContext(Options(name));
        var report = await Service(fresh, loglig).RepairAsync(apply: true);

        Assert.Equal(1, report.Skipped);
        Assert.Equal("SIGRON-MANUAL", (await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 1)).LastNameEn);
        Assert.DoesNotContain(db.AdminAudits, a => a.EntityId == "1");
    }

    // ── Проверка реестра ─────────────────────────────────────────────────────

    [Fact]
    public async Task Check_FindsHebrewAndForeign_HebrewFirst()
    {
        var (db, _) = await SeedAsync();
        var outcome = await new SwimmerEnglishNameCheck(db).RunAsync();

        Assert.Equal(2, outcome.Total);
        Assert.Equal([1, 2], outcome.Items.Select(i => i.EntityId));
        Assert.Equal("иврит в латинском поле", outcome.Items[0].Details);
        Assert.Contains("«ELKIN»", outcome.Items[1].Details);
    }

    private sealed class FakeLoglig(IReadOnlyDictionary<int, string> cards, Action<int>? onCard = null) : ILogligClient
    {
        public Task<string?> GetPlayerEnglishNameAsync(int logligId, CancellationToken ct = default)
        {
            onCard?.Invoke(logligId);
            return Task.FromResult(cards.TryGetValue(logligId, out var c) ? c : null);
        }

        public Task<LogligPlayerCard?> GetPlayerCardAsync(int logligId, int? seasonId = null, CancellationToken ct = default)
            => Task.FromResult<LogligPlayerCard?>(null);

        public string BuildPublicProfileUrl(int logligId, int? seasonId = null, bool resultsTab = false) => "";

        public Task<LogligCompetitionStanding?> GetCompetitionStandingAsync(
            int logligId, int scaleSampleEvents = 12, CancellationToken ct = default)
            => Task.FromResult<LogligCompetitionStanding?>(null);

        public Task<LogligRegulationDoc?> GetRegulationAsync(int logligId, CancellationToken ct = default)
            => Task.FromResult<LogligRegulationDoc?>(null);

        public Task<IReadOnlyList<LogligParticipant>> GetCompetitionParticipantsAsync(
            int competitionLogligId, IReadOnlyCollection<string>? wanted = null,
            int maxEvents = 60, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LogligParticipant>>([]);

        public Task<int?> GetCompetitionSeasonIdAsync(int competitionLogligId, CancellationToken ct = default)
            => Task.FromResult<int?>(null);
    }
}
