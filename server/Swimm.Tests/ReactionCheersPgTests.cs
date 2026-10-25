using Microsoft.EntityFrameworkCore;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Чтение 🎉 пачкой (<see cref="ReactionRepository.GetCheersAsync"/>) — против настоящего
/// Postgres: группировка с условным агрегатом «моё» на InMemory проходит всегда, а в SQL
/// переводится не обязательно. Только чтение; пропуск, если базы нет. Пустая таблица реакций
/// тест не обесценивает — запрос всё равно уходит в Postgres и переводится целиком.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class ReactionCheersPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmDbContext? TryCreate()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).Options);
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    [Fact]
    public async Task GetCheers_TranslatesForGuestAndUser()
    {
        await using var db = TryCreate();
        if (db == null) return; // PG недоступен — пропуск

        var ids = await db.Results.OrderBy(r => r.Id).Select(r => r.Id).Take(50).ToListAsync();
        var repo = new ReactionRepository(db);

        var asGuest = await repo.GetCheersAsync(ids, null);
        var asUser = await repo.GetCheersAsync(ids, 1);

        Assert.All(asGuest, c => { Assert.True(c.Count > 0); Assert.False(c.Mine); });
        Assert.All(asUser, c => Assert.True(c.Count > 0));
    }
}
