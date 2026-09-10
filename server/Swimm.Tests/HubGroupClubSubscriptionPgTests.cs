using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Подписка группы на клуб против НАСТОЯЩЕГО Postgres: то, чего InMemory не видит —
/// advisory-lock (сырой SQL), check-ограничения состава и главный риск П2: пересборка после
/// импорта идёт в ТОМ ЖЕ контексте, у которого только что закоммитили свою транзакцию, внутри
/// retry-стратегии (как в JsonImportService). Пропуск, если PG недоступен.
///
/// Тест ходит в рабочую локальную базу, поэтому заводит свою группу и в finally удаляет её —
/// каскад уносит и состав, и подписку.
/// </summary>
public class HubGroupClubSubscriptionPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            // Та же retry-стратегия, что в приложении: с ней ручная транзакция вне
            // execution strategy бросает исключение — это и проверяем.
            .UseNpgsql(Conn, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3))
            .Options);

    private static SwimmDbContext? TryCreate()
    {
        var db = NewContext();
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    /// <summary>Клуб с пловцами в окне активности и любой владелец для группы; null — база пуста.</summary>
    private static async Task<(int ClubId, int OwnerId)?> PickAsync(SwimmDbContext db)
    {
        var since = HubGroupClubRules.ActivitySince(DateTime.UtcNow);
        var clubId = await db.Results
            .Where(r => r.RelayId == null && r.CompetitionDate >= since)
            .GroupBy(r => r.ClubId)
            .OrderByDescending(g => g.Count())
            .Select(g => (int?)g.Key)
            .FirstOrDefaultAsync();
        var ownerId = await db.AppUsers.OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync();
        return clubId is int c && ownerId is int o ? (c, o) : null;
    }

    [Fact]
    public async Task Subscribe_ThenSyncAfterCommittedImportTransaction_OnPostgres()
    {
        await using var probe = TryCreate();
        if (probe == null) return; // PG недоступен — пропуск
        var picked = await PickAsync(probe);
        if (picked == null) return;
        var (clubId, ownerId) = picked.Value;

        var group = new HubGroup { Name = "PG club-sub test", Slug = $"pg-club-sub-{Guid.NewGuid():N}", OwnerUserId = ownerId };
        probe.HubGroups.Add(group);
        await probe.SaveChangesAsync();

        try
        {
            await using var db = NewContext();
            var svc = new HubGroupClubSubscriptionService(db, new HubGroupCrudCore(db, new NullCache()));

            var subscribed = await svc.SubscribeAsync(group.Id, clubId, ownerId);
            Assert.True(subscribed.Success, subscribed.Error);
            Assert.True(subscribed.Sync.Added > 0);

            // Одного клубного пловца «теряем» мимо сервиса — пересборка обязана вернуть.
            var victim = await db.HubGroupMembers.AsNoTracking()
                .Where(m => m.HubGroupId == group.Id).OrderBy(m => m.Id).FirstAsync();
            await db.HubGroupMembers.Where(m => m.Id == victim.Id).ExecuteDeleteAsync();

            // Сценарий импорта: своя транзакция закоммичена, контекст тот же, мы всё ещё внутри
            // retry-стратегии — пересборка должна отработать, а не упасть на «чужой» транзакции.
            var strategy = db.Database.CreateExecutionStrategy();
            var sync = await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.Database.ExecuteSqlRawAsync("SELECT 1");
                await tx.CommitAsync();

                db.ChangeTracker.Clear();
                return await svc.SyncClubsAsync([clubId]);
            });

            Assert.Equal(1, sync.Added);
            Assert.True(await db.HubGroupMembers.AnyAsync(m => m.HubGroupId == group.Id && m.SwimmerId == victim.SwimmerId));

            // Отписка уносит все клубные строки.
            var removed = await svc.UnsubscribeAsync(group.Id);
            Assert.Equal(subscribed.Sync.Added, removed!.Removed);
            Assert.False(await db.HubGroupMembers.AnyAsync(m => m.HubGroupId == group.Id));
        }
        finally
        {
            await using var clean = NewContext();
            await clean.HubGroups.Where(g => g.Id == group.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task CheckConstraints_RejectHiddenManualAndUnknownSource()
    {
        await using var probe = TryCreate();
        if (probe == null) return;
        var picked = await PickAsync(probe);
        if (picked == null) return;
        var swimmerId = await probe.Results.Select(r => r.SwimmerId).FirstAsync();

        var group = new HubGroup { Name = "PG member CK test", Slug = $"pg-member-ck-{Guid.NewGuid():N}", OwnerUserId = picked.Value.OwnerId };
        probe.HubGroups.Add(group);
        await probe.SaveChangesAsync();

        try
        {
            // «Ручной и скрытый» — противоречие, БД его не принимает (CK_HubGroupMembers_ExcludedOnlyClub).
            await using (var db = NewContext())
            {
                db.HubGroupMembers.Add(new HubGroupMember
                {
                    HubGroupId = group.Id, SwimmerId = swimmerId,
                    Source = HubGroupMemberSource.Manual, IsExcluded = true
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            // Неизвестный источник — тоже (CK_HubGroupMembers_Source).
            await using (var db = NewContext())
            {
                db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = group.Id, SwimmerId = swimmerId, Source = "import" });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            // Вторая подписка той же группы — уникальный индекс (одна подписка на группу).
            await using (var db = NewContext())
            {
                db.HubGroupClubSubscriptions.Add(new HubGroupClubSubscription { HubGroupId = group.Id, ClubId = picked.Value.ClubId });
                await db.SaveChangesAsync();
                db.HubGroupClubSubscriptions.Add(new HubGroupClubSubscription { HubGroupId = group.Id, ClubId = picked.Value.ClubId });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
        }
        finally
        {
            await using var clean = NewContext();
            await clean.HubGroups.Where(g => g.Id == group.Id).ExecuteDeleteAsync();
        }
    }
}
