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
/// <see cref="SwimmerCurrentClubRuleTests"/>; здесь — запись карточек и журнала.
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
    }

    [Fact]
    public async Task Apply_ChangesCard_WritesJournal_SecondRunIsNoop()
    {
        await using var db = CreateDb(nameof(Apply_ChangesCard_WritesJournal_SecondRunIsNoop));
        await SeedAsync(db);
        var svc = new SwimmerClubRefreshService(db);

        await svc.RefreshAsync(null, SwimmerClubChangeSources.Cli, apply: true);
        var again = await svc.RefreshAsync(null, SwimmerClubChangeSources.Cli, apply: true);

        Assert.Equal(Raanana, (await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 6073)).ClubId);
        var log = Assert.Single(db.SwimmerClubChanges);
        Assert.Equal((6073, (int?)Dolphin, (int?)Raanana, "cli"), (log.SwimmerId, log.FromClubId, log.ToClubId, log.Source));
        Assert.Empty(again.Changes);
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
    }

    [Fact]
    public async Task OnlyGivenSwimmers_AreRecomputed()
    {
        await using var db = CreateDb(nameof(OnlyGivenSwimmers_AreRecomputed));
        await SeedAsync(db);

        var report = await new SwimmerClubRefreshService(db).RefreshAsync([7000], SwimmerClubChangeSources.Import, apply: true);

        Assert.Empty(report.Changes);
        Assert.Equal(Dolphin, (await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 6073)).ClubId);
    }

    [Fact]
    public async Task Admin_SetsClubAndLock_WritesJournal_AndRefreshKeepsIt()
    {
        await using var db = CreateDb(nameof(Admin_SetsClubAndLock_WritesJournal_AndRefreshKeepsIt));
        await SeedAsync(db);
        var svc = new SwimmerClubRefreshService(db);

        var (result, error) = await svc.SetByAdminAsync(7000, Raanana, locked: true);
        Assert.Equal(SwimmerClubSetError.None, error);
        Assert.Equal((Dolphin, Raanana), (result!.FromClubId!.Value, result.ToClubId!.Value));

        // Правило вернуло бы Дельфин (последний старт), но клуб закреплён.
        await svc.RefreshAsync(null, SwimmerClubChangeSources.Cli, apply: true);
        var card = await db.Swimmers.AsNoTracking().SingleAsync(s => s.Id == 7000);
        Assert.Equal((Raanana, true), (card.ClubId!.Value, card.ClubLocked));
        Assert.Contains(db.SwimmerClubChanges, c => c.SwimmerId == 7000 && c.Source == "admin" && c.ToClubId == Raanana);

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
    public async Task Import_MovesSwimmerToNewClub_ReimportAndOlderMeetDoNotTwitch()
    {
        await using var db = CreateDb(nameof(Import_MovesSwimmerToNewClub_ReimportAndOlderMeetDoNotTwitch));
        JsonImportService Import() => new(db, new NullCache(), clubRefresh: new SwimmerClubRefreshService(db));

        await Import().ImportAsync(Json("Summer meet", "20/07/2026", "Dolphin Netanya"));
        var swimmer = await db.Swimmers.AsNoTracking().SingleAsync();
        var dolphin = (await db.Clubs.AsNoTracking().SingleAsync(c => c.Name == "Dolphin Netanya")).Id;
        Assert.Equal(dolphin, swimmer.ClubId);

        var eilat = await Import().ImportAsync(Json("Eilat", "05/11/2026", "Raanana"));
        var raanana = (await db.Clubs.AsNoTracking().SingleAsync(c => c.Name == "Raanana")).Id;
        Assert.Equal(raanana, (await db.Swimmers.AsNoTracking().SingleAsync()).ClubId);
        Assert.Contains(eilat.DiagnosticLog, l => l.StartsWith("Клуб пловца сменился у 1"));
        var log = Assert.Single(db.SwimmerClubChanges);
        Assert.Equal(("import", (int?)dolphin, (int?)raanana), (log.Source, log.FromClubId, log.ToClubId));

        // Повторный импорт Эйлата и запоздалый импорт старого старта клуб не дёргают.
        await Import().ImportAsync(Json("Eilat", "05/11/2026", "Raanana"));
        await Import().ImportAsync(Json("Spring meet", "10/03/2026", "Dolphin Netanya"));
        Assert.Equal(raanana, (await db.Swimmers.AsNoTracking().SingleAsync()).ClubId);
        Assert.Single(db.SwimmerClubChanges);
    }
}
