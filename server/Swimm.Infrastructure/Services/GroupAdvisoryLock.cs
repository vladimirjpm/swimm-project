using Microsoft.EntityFrameworkCore;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Работа над данными одной группы в транзакции под её advisory-lock (образец —
/// HubGroupClubSubscriptionService). Ключ блокировки — пара (класс, id группы); класс — своя
/// константа у каждого вида работ, лишь бы не совпала с другими блокировками в БД.
/// Снимается сама на commit/rollback. На InMemory (тесты) транзакций и блокировок нет — там
/// один поток, работа идёт как есть.
/// </summary>
internal static class GroupAdvisoryLock
{
    public static async Task<T> InGroupTransactionAsync<T>(
        SwimmDbContext db, int lockClass, int hubGroupId, Func<Task<T>> work)
    {
        if (!db.Database.IsNpgsql()) return await work();

        // Уже внутри чужой транзакции — встаём в неё и берём только блокировку.
        if (db.Database.CurrentTransaction != null)
        {
            await LockAsync(db, lockClass, hubGroupId);
            return await work();
        }

        // Execution strategy обязательна: ручная транзакция при retry-стратегии иначе бросает.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await LockAsync(db, lockClass, hubGroupId);
            var result = await work();
            await tx.CommitAsync();
            return result;
        });
    }

    private static Task LockAsync(SwimmDbContext db, int lockClass, int hubGroupId) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockClass}, {hubGroupId})");
}
