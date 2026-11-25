using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Р124 против настоящего Postgres: шаг после импорта дня-соседа собирает лежащий одиночный день и
/// новый в событие НАСТОЯЩИМ <see cref="CompetitionDayJoinService"/> с общим именем — оба дня
/// переименовываются под уникальным <c>(Name, Date, PoolType)</c>, compID поднимается на событие
/// (частичный уникальный индекс), и круг дней compID (Р115) видит оба. InMemory этих ограничений не
/// видит. Заводит свои строки и удаляет их в finally; пропуск, если базы нет.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class SiblingDayPullPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmDbContext? TryCreate()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).Options);
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    private sealed class NoRecalc : ICompetitionRecalculationService
    {
        public Task<int> RecalculateCompetitionAsync(int competitionId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RecalculateAllCombinedAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RecalculateAllParaPointsAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    [Fact]
    public async Task Complete_SingleDay_JoinsUnderCommonName_CompIdOnEvent_BothDaysResolve()
    {
        await using var db = TryCreate();
        if (db == null) return; // PG недоступен — пропуск

        var tag = $"pg-sib-{Guid.NewGuid():N}"[..18];
        var orgCompId = 900_000_000 + Random.Shared.Next(1, 99_999_999);
        var fri = new Competition { Name = $"{tag} יום שישי 6.12.99", Date = "06/12/2099", PoolType = "25m", OrgCompId = orgCompId };
        var sat = new Competition { Name = $"{tag} יום שבת 7.12.99", Date = "07/12/2099", PoolType = "25m" };
        db.Competitions.AddRange(fri, sat);
        await db.SaveChangesAsync();
        int? eventId = null;
        try
        {
            var service = new SiblingDayPullService(db, Mock.Of<IProgrammeCheckService>(), Mock.Of<ICompetitionDiscoveryProvider>(),
                Mock.Of<IDiscoveryPreviewService>(), Mock.Of<IImportJobQueue>(), new CompetitionDayJoinService(db, new NoRecalc()),
                Mock.Of<IAdminAuditService>(), NullLogger<SiblingDayPullService>.Instance);

            var done = await service.CompleteAsync(
                new SiblingDayTarget(1, orgCompId, 11590, fri.Id, null, tag, "07/12/2099", "utest"),
                new ImportResult { Created = 1, CompetitionIds = [sat.Id] });

            Assert.True(done.Joined, done.Message);
            eventId = done.EventId;
            db.ChangeTracker.Clear();
            var days = await db.Competitions.AsNoTracking().Where(c => c.EventId == eventId).OrderBy(c => c.DayNumber).ToListAsync();
            Assert.Equal([fri.Id, sat.Id], days.Select(d => d.Id));
            Assert.All(days, d => Assert.Equal(tag, d.Name));
            Assert.Equal($"{tag} יום שישי 6.12.99", days[0].SubName);
            Assert.Equal($"{tag} יום שבת 7.12.99", days[1].SubName);
            Assert.Equal(orgCompId, days[0].OrgCompId); // штамп остаётся на своём дне (как 488: у 1899 пусто)
            Assert.Null(days[1].OrgCompId);
            var ev = await db.CompetitionEvents.AsNoTracking().SingleAsync(e => e.Id == eventId);
            Assert.Equal(orgCompId, ev.OrgCompId);
            // Р115 считает дни этим кругом — теперь «2 из 2».
            Assert.Equal(2, (await CompetitionIdentity.ResolveDaysAsync(db, orgCompId)).Count);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Competitions.Where(c => c.Id == fri.Id || c.Id == sat.Id).ExecuteDeleteAsync();
            if (eventId is int id) await db.CompetitionEvents.Where(e => e.Id == id).ExecuteDeleteAsync();
        }
    }
}
