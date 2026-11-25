using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Swimm.Infrastructure.Services.DataChecks;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Р125/Р126 на настоящем Postgres: новые запросы обязаны ПЕРЕВОДИТЬСЯ в SQL — на InMemory
/// непереводимое выражение проходит молча (урок RelayMemberUpsertPgTests). Утверждаем факт
/// выполнения, а не числа: база живая. Все вызовы читающие — в базу тест ничего не пишет.
///
/// Пропускается, если Postgres недоступен.
/// </summary>
public class LogligCardGenderPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static SwimmDbContext? TryCreate()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).Options);
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    private static GenderImpactService Impact(SwimmDbContext db) =>
        new(db, new ResultRepository(
                new SwimmReadDbContext(new DbContextOptionsBuilder<SwimmReadDbContext>().UseNpgsql(Conn).Options), new NullCache()),
            NullLogger<GenderImpactService>.Instance);

    [Fact]
    public async Task GenderVsLogligCheck_RunsAgainstRealDatabase()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var outcome = await new GenderVsLogligCheck(db).RunAsync();

        Assert.True(outcome.Items.Count <= outcome.Total);
    }

    [Fact]
    public async Task ImportSwimmersQuery_AndPendingReviews_Translate()
    {
        await using var db = TryCreate();
        if (db == null) return;
        var impact = Impact(db);
        var finder = new LogligGenderReviewFinder(db, impact);
        var service = new LogligCardGenderService(db, Mock.Of<ILogligClient>(), Mock.Of<IDataCheckRunner>(),
            Mock.Of<IAdminAuditService>(), impact, finder, new LogligMatchService(),
            NullLogger<LogligCardGenderService>.Instance);

        // Выборка «пловцы импорта» — по compID и по дню.
        var compId = await db.Competitions.AsNoTracking().Where(c => c.OrgCompId != null)
            .OrderByDescending(c => c.Id).Select(c => c.OrgCompId).FirstOrDefaultAsync();
        var byComp = await service.ImportSwimmers(db.Results.Where(r => r.Competition!.OrgCompId == compId))
            .Select(s => s.Id).Take(5).ToListAsync();
        var dayId = await db.Competitions.AsNoTracking().OrderByDescending(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync();
        var byDay = await service.ImportSwimmers(db.Results.Where(r => r.CompetitionId == dayId))
            .Select(s => s.Id).Take(5).ToListAsync();
        Assert.True(byComp.Count <= 5 && byDay.Count <= 5);

        // Находка ручной проверки и «игрок loglig у строки».
        Assert.NotNull(await finder.GetPendingAsync());
        Assert.False(await finder.RowPlayerMatchedAsync(-1, -1, CancellationToken.None));
    }

    // Сухой расчёт на живом пловце: карточки Overview, объединённые места, пара-очки, Season best.
    [Fact]
    public async Task ImpactAssessment_RunsAgainstRealDatabase()
    {
        await using var db = TryCreate();
        if (db == null) return;

        // Пловец с личной строкой без пола, если такой есть; иначе любой — запросы всё равно выполнятся.
        var swimmerId = await db.Results.AsNoTracking()
            .Where(r => r.RelayId == null && (r.Gender == null || r.Gender == ""))
            .Select(r => (int?)r.SwimmerId).FirstOrDefaultAsync()
            ?? await db.Results.AsNoTracking().Where(r => r.RelayId == null).Select(r => r.SwimmerId).FirstAsync();

        var impact = await Impact(db).AssessAsync(swimmerId, "male");

        Assert.DoesNotContain(impact.Serious, i => i.Code == GenderImpactRules.NotComputed);
    }

    // Условная запись: блокировка строки (FOR UPDATE) внутри стратегии выполнения. Пловец с уже
    // проставленным полом и несуществующий — оба пути читают и ничего не пишут.
    [Fact]
    public async Task ConditionalGenderWrite_LocksAndRefuses_WithoutWriting()
    {
        await using var db = TryCreate();
        if (db == null) return;
        var known = await db.Swimmers.AsNoTracking().Where(s => s.Gender == "female")
            .Select(s => new { s.Id, s.Gender }).FirstOrDefaultAsync();
        var runner = new DataCheckRunner(db, []);

        Assert.Null(await runner.SetSwimmerGenderIfEmptyAsync(-1, "male"));
        if (known is null) return;
        Assert.Null(await runner.SetSwimmerGenderIfEmptyAsync(known.Id, "male"));
        Assert.Equal("female", await db.Swimmers.AsNoTracking().Where(s => s.Id == known.Id).Select(s => s.Gender).FirstAsync());
    }
}
