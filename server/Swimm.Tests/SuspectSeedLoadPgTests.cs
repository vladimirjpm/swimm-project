using Microsoft.EntityFrameworkCore;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Подгрузка заявочных времён для скана качества (правило seed_time_jump, И-55) — против
/// настоящего Postgres: подзапрос «Id строк скоупа» с приведением к <c>long?</c> под
/// nullable <c>CompetitionEntries.ResultId</c> на InMemory проходит всегда, а в SQL
/// переводится не обязательно. Только чтение; пропуск, если базы нет или заявок в ней нет.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class SuspectSeedLoadPgTests
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
    public async Task LoadSeeds_TranslatesAndMatchesEntries()
    {
        await using var db = TryCreate();
        if (db == null) return; // PG недоступен — пропуск

        var competitionId = await db.CompetitionEntries
            .Where(e => e.ResultId != null && e.SeedTimeMs > 0)
            .Select(e => e.CompetitionId)
            .FirstOrDefaultAsync();
        if (competitionId is not { } cid) return; // заявок со сшивкой в базе нет

        var seeds = await SuspectResultService.LoadSeedsAsync(
            db, db.Results.Where(r => r.CompetitionId == cid), default);

        Assert.NotEmpty(seeds);
        // Ключи — строки именно этого дня, и у каждой — сид из её заявки.
        var ids = seeds.Keys.ToList();
        Assert.Equal(ids.Count,
            await db.Results.CountAsync(r => r.CompetitionId == cid && ids.Contains(r.Id)));
        Assert.All(seeds.Values, s =>
        {
            Assert.True(s.SeedTimeMs > 0);
            Assert.Matches(@"^\d+/\d+$", s.HeatKey);
        });
    }
}
