using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Текущий клуб в карточке пловца (Р95, Ф1 docs/plans/swimmer-club-change-plan.md): догон и
/// dry-run, закрепление, ручная правка с журналом, пересчёт после импорта. Само правило —
/// <see cref="SwimmerCurrentClubRuleTests"/>; здесь — запись кандидатов, карточек и журнала.
/// С Р98 пересчёт карточку не меняет — только заводит кандидата; сверку с loglig проверяет
/// <see cref="SwimmerClubLogligCheckServiceTests"/>.
/// </summary>
public class SwimmerClubRefreshServiceTests
{
    private const int Dolphin = 438, Raanana = 449, Israel = 900;

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    /// <summary>Пловец 6073 с Дельфином в карточке: летом за Дельфин, в ноябре за Раанану.</summary>
    private static async Task SeedAsync(SwimmDbContext db, bool locked = false)
    {
        db.Clubs.AddRange(
            new Club { Id = Dolphin, Name = "הפועל דולפין נתניה" },
            new Club { Id = Raanana, Name = "ע.ל.ה עצמה רעננה" },
            new Club { Id = Israel, Name = "Israel", IsPseudo = true });
        db.Styles.Add(new Style { Id = 1, Name = "Freestyle" });
        db.Competitions.AddRange(
            new Competition { Id = 1800, Name = "Summer", Date = "2026-07-20" },
            new Competition { Id = 1874, Name = "Eilat d1", Date = "2026-11-05" },
            new Competition { Id = 1900, Name = "National team", Date = "2026-11-10" });
        db.Swimmers.AddRange(
            new Swimmer { Id = 6073, FirstName = "ויקטוריה", LastName = "זכרוב", ClubId = Dolphin, ClubLocked = locked },
            new Swimmer { Id = 7000, FirstName = "שקט", LastName = "שקט", ClubId = Dolphin });
        db.Results.AddRange(
            Row(1, 6073, Dolphin, 1800, "2026-07-20"),
            Row(2, 6073, Raanana, 1874, "2026-11-05"),
            Row(3, 6073, Israel, 1900, "2026-11-10"),   // сборная — не клуб пловца
            Row(4, 7000, Dolphin, 1874, "2026-11-05"));
        await db.SaveChangesAsync();
    }

    private static ResultRecord Row(long id, int swimmer, int club, int competition, string date) => new()
    {
        Id = id, SwimmerId = swimmer, ClubId = club, CompetitionId = competition, StyleId = 1,
        CompetitionDate = DateTime.Parse(date), Distance = "50", Gender = "female",
    };

    [Fact]
    public async Task DryRun_ReportsChange_WritesNothing()
    {
        await using var db = CreateDb(nameof(DryRun_ReportsChange_WritesNothing));
        await SeedAsync(db);

        var report = await new SwimmerClubRefreshService(db).RefreshAsync(null, SwimmerClubChangeSources.Cli, apply: false);

        var change = Assert.Single(report.Changes);
        Assert.Equal((6073, Dolphin, Raanana), (change.SwimmerId, change.FromClubId, change.ToClubId));
        Assert.Equal(new DateOnly(2026, 11, 5), change.LastSwimDate); // старт за сборную не в счёт
        Assert.Equal(2, report.Checked);
        Assert.False(report.Applied);
        Assert.Equal(Dolphin, (await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 6073)).ClubId);
        Assert.Empty(db.SwimmerClubChanges);
        Assert.Empty(db.SwimmerClubCandidates);
    }

    [Fact]
    public async Task Apply_WritesCandidate_NotCard_SecondRunKeepsOneCandidate()
    {
        await using var db = CreateDb(nameof(Apply_WritesCandidate_NotCard_SecondRunKeepsOneCandidate));
        await SeedAsync(db);
        var signal = new CountingSignal();
        var svc = new SwimmerClubRefreshService(db, signal);

        await svc.RefreshAsync(null, SwimmerClubChangeSources.Cli, apply: true);
        var again = await svc.RefreshAsync(null, SwimmerClubChangeSources.Cli, apply: true);

        // Р98: карточку меняет только подтверждение loglig.
        Assert.Equal(Dolphin, (await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 6073)).ClubId);
        Assert.Empty(db.SwimmerClubChanges);
        var c = Assert.Single(db.SwimmerClubCandidates);
        Assert.Equal((6073, (int?)Dolphin, Raanana, "cli", (string?)null),
            (c.SwimmerId, c.FromClubId, c.ToClubId, c.Source, c.Verdict));
        Assert.Equal(new DateOnly(2026, 11, 5), c.LastSwimDate);
        Assert.Single(again.Changes);         // кандидат всё ещё в плане, но не задвоился
        Assert.Equal(0, signal.Count);        // догон CLI фон не будит — сверяет сам
    }

    [Fact]
    public async Task PendingCandidate_IsWithdrawn_WhenNextStartIsForCardClub()
    {
        await using var db = CreateDb(nameof(PendingCandidate_IsWithdrawn_WhenNextStartIsForCardClub));
        await SeedAsync(db);
        var svc = new SwimmerClubRefreshService(db);
        await svc.RefreshAsync(null, SwimmerClubChangeSources.Cli, apply: true);
        Assert.Single(db.SwimmerClubCandidates);

        // Следующий старт — снова за Дельфин (клуб из карточки): кандидат снимается сам.
        db.Competitions.Add(new Competition { Id = 1950, Name = "Winter", Date = "2026-12-01" });
        db.Results.Add(Row(10, 6073, Dolphin, 1950, "2026-12-01"));
        await db.SaveChangesAsync();
        var report = await svc.RefreshAsync([6073], SwimmerClubChangeSources.Import, apply: true);

        Assert.Empty(report.Changes);
        Assert.Equal(1, report.Withdrawn);
        Assert.Empty(db.SwimmerClubCandidates);
    }

    [Fact]
    public async Task CandidateToClubChanges_ResetsVerdict_KeepsLogligCache()
    {
        await using var db = CreateDb(nameof(CandidateToClubChanges_ResetsVerdict_KeepsLogligCache));
        await SeedAsync(db);
        db.SwimmerClubCandidates.Add(new SwimmerClubCandidate
        {
            SwimmerId = 6073, FromClubId = Dolphin, ToClubId = 777, Source = "import", Verdict = "other",
            LogligId = 55, LogligClubName = "ע.ל.ה עצמה רעננה", CheckedAt = DateTime.UtcNow,
            LastSwimDate = new DateOnly(2026, 10, 1),
        });
        await db.SaveChangesAsync();
        var signal = new CountingSignal();

        await new SwimmerClubRefreshService(db, signal).RefreshAsync([6073], SwimmerClubChangeSources.Import, apply: true);

        var c = await db.SwimmerClubCandidates.AsNoTracking().SingleAsync();
        Assert.Equal((Raanana, (string?)null, "ע.ל.ה עצמה רעננה"), (c.ToClubId, c.Verdict, c.LogligClubName));
        Assert.Equal(1, signal.Count); // импорт будит фоновую сверку
    }

    private sealed class CountingSignal : ISwimmerClubCheckSignal
    {
        public int Count { get; private set; }
        public void Notify() => Count++;
        public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => Task.FromResult(false);
    }

    [Fact]
    public async Task Locked_IsNotTouched()
    {
        await using var db = CreateDb(nameof(Locked_IsNotTouched));
        await SeedAsync(db, locked: true);

        var report = await new SwimmerClubRefreshService(db).RefreshAsync(null, SwimmerClubChangeSources.Cli, apply: true);

        Assert.Empty(report.Changes);
        Assert.Equal(1, report.Locked);
        Assert.Equal(Dolphin, (await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 6073)).ClubId);
        Assert.Empty(db.SwimmerClubCandidates);
    }

    [Fact]
    public async Task OnlyGivenSwimmers_AreRecomputed()
    {
        await using var db = CreateDb(nameof(OnlyGivenSwimmers_AreRecomputed));
        await SeedAsync(db);

        var report = await new SwimmerClubRefreshService(db).RefreshAsync([7000], SwimmerClubChangeSources.Import, apply: true);

        Assert.Empty(report.Changes);
        Assert.Equal(Dolphin, (await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 6073)).ClubId);
        Assert.Empty(db.SwimmerClubCandidates);
    }

    [Fact]
    public async Task Admin_SetsClubAndLock_WritesJournal_AndRefreshKeepsIt()
    {
        await using var db = CreateDb(nameof(Admin_SetsClubAndLock_WritesJournal_AndRefreshKeepsIt));
        await SeedAsync(db);
        var svc = new SwimmerClubRefreshService(db);

        db.SwimmerClubCandidates.Add(new SwimmerClubCandidate
            { SwimmerId = 7000, FromClubId = Dolphin, ToClubId = Raanana, Source = "import" });
        await db.SaveChangesAsync();

        var (result, error) = await svc.SetByAdminAsync(7000, Raanana, locked: true);
        Assert.Empty(db.SwimmerClubCandidates); // человек решил сам — кандидат снят
        Assert.Equal(SwimmerClubSetError.None, error);
        Assert.Equal((Dolphin, Raanana), (result!.FromClubId!.Value, result.ToClubId!.Value));

        // Правило вернуло бы Дельфин (последний старт), но клуб закреплён.
        await svc.RefreshAsync(null, SwimmerClubChangeSources.Cli, apply: true);
        var card = await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 7000);
        Assert.Equal((Raanana, true), (card.ClubId!.Value, card.ClubLocked));
        Assert.Contains(db.SwimmerClubChanges, c => c.SwimmerId == 7000 && c.Source == "admin" && c.ToClubId == Raanana);
        Assert.DoesNotContain(db.SwimmerClubCandidates, c => c.SwimmerId == 7000); // закреплённому кандидата не заводят

        var admin = await svc.GetForAdminAsync(7000);
        Assert.Equal(Dolphin, admin!.RuleClubId);
        Assert.True(admin.ClubLocked);
        Assert.Single(admin.History);
    }

    [Fact]
    public async Task Admin_RejectsPseudoClub_AndUnknownClub()
    {
        await using var db = CreateDb(nameof(Admin_RejectsPseudoClub_AndUnknownClub));
        await SeedAsync(db);
        var svc = new SwimmerClubRefreshService(db);

        Assert.Equal(SwimmerClubSetError.PseudoClub, (await svc.SetByAdminAsync(7000, Israel, false)).Error);
        Assert.Equal(SwimmerClubSetError.ClubNotFound, (await svc.SetByAdminAsync(7000, 12345, false)).Error);
        Assert.Equal(SwimmerClubSetError.SwimmerNotFound, (await svc.SetByAdminAsync(1, Dolphin, false)).Error);
        Assert.Empty(db.SwimmerClubChanges);
    }

    [Fact]
    public async Task Admin_LockOnly_WritesNoJournal()
    {
        await using var db = CreateDb(nameof(Admin_LockOnly_WritesNoJournal));
        await SeedAsync(db);

        await new SwimmerClubRefreshService(db).SetByAdminAsync(7000, Dolphin, locked: true);

        Assert.True((await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 7000)).ClubLocked);
        Assert.Empty(db.SwimmerClubChanges);
    }

    [Fact]
    public async Task RecentChanges_NewestFirst_WithNames()
    {
        await using var db = CreateDb(nameof(RecentChanges_NewestFirst_WithNames));
        await SeedAsync(db);
        db.SwimmerClubChanges.AddRange(
            new SwimmerClubChange { SwimmerId = 6073, FromClubId = Dolphin, ToClubId = Raanana, Source = "import", ChangedAt = DateTime.UtcNow.AddDays(-1) },
            new SwimmerClubChange { SwimmerId = 7000, FromClubId = Raanana, ToClubId = Dolphin, Source = "admin", ChangedAt = DateTime.UtcNow },
            new SwimmerClubChange { SwimmerId = 7000, FromClubId = Dolphin, ToClubId = Raanana, Source = "cli", ChangedAt = DateTime.UtcNow.AddDays(-30) });
        await db.SaveChangesAsync();

        var list = await new SwimmerClubRefreshService(db).RecentChangesAsync(7, 20);

        Assert.Equal([7000, 6073], list.Select(c => c.SwimmerId));
        Assert.Equal("ויקטוריה זכרוב", list[1].SwimmerName);
        Assert.Equal("ע.ל.ה עצמה רעננה", list[1].ToClubName);
    }

    // ── Импорт ───────────────────────────────────────────────────────────────

    private static MemoryStream Json(string competition, string date, string club) =>
        new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]
        {
            new
            {
                country = "ISR", competition, date,
                event_style_name = "Freestyle", event_style_len = "50", event_style_gender = "female",
                pool_type = "25m", position = 1, heat = 1, lane = 1,
                last_name = "Zakharov", first_name = "Victoria", birth_year = 2012,
                club, time = "00:30.00",
            },
        })));

    [Fact]
    public async Task Import_WritesCandidate_CardWaitsForLoglig_ReimportAndOlderMeetDoNotTwitch()
    {
        await using var db = CreateDb(nameof(Import_WritesCandidate_CardWaitsForLoglig_ReimportAndOlderMeetDoNotTwitch));
        var signal = new CountingSignal();
        JsonImportService Import() => new(db, new NullCache(), clubRefresh: new SwimmerClubRefreshService(db, signal));

        await Import().ImportAsync(Json("Summer meet", "20/07/2026", "Dolphin Netanya"));
        var swimmer = await db.Swimmers.AsNoTracking().SingleAsync();
        var dolphin = (await db.Clubs.AsNoTracking().SingleAsync(c => c.Name == "Dolphin Netanya")).Id;
        Assert.Equal(dolphin, swimmer.ClubId);

        // Р98: импорт карточку не меняет — заводит кандидата и будит сверку с loglig.
        var eilat = await Import().ImportAsync(Json("Eilat", "05/11/2026", "Raanana"));
        var raanana = (await db.Clubs.AsNoTracking().SingleAsync(c => c.Name == "Raanana")).Id;
        Assert.Equal(dolphin, (await db.Swimmers.AsNoTracking().SingleAsync()).ClubId);
        Assert.Contains(eilat.DiagnosticLog, l => l.StartsWith("Кандидатов на смену клуба: 1"));
        Assert.Empty(db.SwimmerClubChanges);
        var c = Assert.Single(db.SwimmerClubCandidates);
        Assert.Equal(("import", (int?)dolphin, raanana), (c.Source, c.FromClubId, c.ToClubId));
        Assert.Equal(1, signal.Count);

        // Повторный импорт Эйлата и запоздалый импорт старого старта кандидата не дёргают.
        await Import().ImportAsync(Json("Eilat", "05/11/2026", "Raanana"));
        await Import().ImportAsync(Json("Spring meet", "10/03/2026", "Dolphin Netanya"));
        Assert.Equal(dolphin, (await db.Swimmers.AsNoTracking().SingleAsync()).ClubId);
        Assert.Equal(raanana, (await db.SwimmerClubCandidates.AsNoTracking().SingleAsync()).ToClubId);
        Assert.Equal(1, signal.Count);

        // Следующий старт снова за Дельфин — кандидат снимается.
        var winter = await Import().ImportAsync(Json("Winter", "01/12/2026", "Dolphin Netanya"));
        Assert.Empty(db.SwimmerClubCandidates);
        Assert.Contains(winter.DiagnosticLog, l => l.StartsWith("Снято ждущих кандидатов на смену клуба: 1"));
    }
}
