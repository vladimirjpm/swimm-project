using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// PG-специфичная регрессия для чистки пловцов-сирот при импорте. FK RelayMember→Swimmer
/// стоит Restrict: пловца, оставшегося ногой эстафеты (RelayMember) без индивидуальных
/// результатов, НЕЛЬЗЯ удалить как сироту — иначе импорт падает «An error occurred while
/// saving the entity changes». Фильтр сиротства должен учитывать RelayMembers. InMemory FK
/// Restrict не enforce'ит (баг там невидим) → тест против реального Postgres, пропуск если недоступен.
/// </summary>
public class RelayMemberUpsertPgTests
{
    private const string Conn =
        "Host=localhost;Port=5432;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmDbContext? TryCreate()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).Options);
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    [Fact]
    public async Task OrphanCleanup_SkipsSwimmerStillReferencedByRelayMember()
    {
        await using var db = TryCreate();
        if (db == null) return; // PG недоступен — пропуск

        // Пловец-нога без индивидуальных результатов (как после перематчинга владельца эстафеты).
        var swimmer = new Swimmer { LastName = "PGOrphanTest", FirstName = "Leg", LastNameEn = "", FirstNameEn = "", BirthYear = 2014 };
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();
        var relay = new Relay { TeamName = "PG-Test", SwimmersName = "leg" };
        relay.Members.Add(new RelayMember { SwimmerId = swimmer.Id, LegOrder = 1 });
        db.Relays.Add(relay);
        await db.SaveChangesAsync();
        var sid = swimmer.Id; var rid = relay.Id;

        try
        {
            // (1) Хазард: прямое удаление такого пловца падает по FK Restrict RelayMember→Swimmer.
            await using (var ctx = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).Options))
            {
                await Assert.ThrowsAnyAsync<Exception>(() =>
                    ctx.Swimmers.Where(s => s.Id == sid).ExecuteDeleteAsync());
            }

            // (2) Исправленный фильтр сиротства (с проверкой RelayMembers) НЕ считает его сиротой.
            await using (var ctx = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).Options))
            {
                var wouldDelete = await ctx.Swimmers
                    .Where(s => s.Id == sid)
                    .Where(s => !ctx.Results.Any(r => r.SwimmerId == s.Id)
                        && !ctx.RelayMembers.Any(m => m.SwimmerId == s.Id)   // guard, добавленный в фикс
                        && !ctx.HubGroupMembers.Any(m => m.SwimmerId == s.Id)
                        && !ctx.UserFavorites.Any(f => f.SwimmerId == s.Id)
                        && !ctx.UserMedia.Any(m => m.SwimmerId == s.Id)
                        && !ctx.AppUsers.Any(u => u.SwimmerId == s.Id))
                    .AnyAsync();
                Assert.False(wouldDelete, "пловец-нога не должен попадать под удаление сирот");
            }
        }
        finally
        {
            await using var clean = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).Options);
            await clean.RelayMembers.Where(m => m.RelayId == rid).ExecuteDeleteAsync();
            await clean.Relays.Where(r => r.Id == rid).ExecuteDeleteAsync();
            await clean.Swimmers.Where(s => s.Id == sid).ExecuteDeleteAsync();
        }
    }
}
