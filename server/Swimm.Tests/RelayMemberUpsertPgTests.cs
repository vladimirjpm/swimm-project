using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// PG-специфичная регрессия для чистки пловцов-сирот. FK RelayMember→Swimmer стоит Restrict:
/// пловца, оставшегося ногой эстафеты (RelayMember) без индивидуальных результатов, НЕЛЬЗЯ
/// удалить как сироту — иначе импорт падает «An error occurred while saving the entity changes».
/// Критерий сиротства должен учитывать RelayMembers. InMemory FK Restrict не enforce'ит
/// (баг там невидим) → тест против реального Postgres, пропуск если недоступен.
///
/// Проверяем РАБОЧИЙ критерий — через SwimmerDedupService.DeleteOrphansAsync (единственная
/// публичная дверь к OrphanQuery). Раньше здесь лежала инлайновая копия предиката: она была
/// «правильной» сама по себе и потому не заметила, что в сервисе проверки RelayMembers нет
/// (2026-08-02: 102 живых эстафетчика в списке сирот). Копию предиката не возвращать.
/// </summary>
public class RelayMemberUpsertPgTests
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

            // (2) Сервис сиротой его не считает. Ids передаём явно — иначе прогон вычистил бы
            // всех настоящих сирот рабочей базы (тест ходит в неё, а не в песочницу).
            await using (var ctx = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).Options))
            {
                var report = await new SwimmerDedupService(ctx).DeleteOrphansAsync([sid]);

                Assert.Equal(0, report.Deleted);
                Assert.Contains(sid, report.SkippedIds);
                Assert.True(await ctx.Swimmers.AnyAsync(s => s.Id == sid),
                    "пловец-нога не должен попадать под удаление сирот");
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
