using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Склейка переносит заявки стартового протокола — против настоящего Postgres: FK
/// <c>CompetitionEntries → Swimmers</c> = Restrict, и до 08.10.2026 удаление дубля с заявками падало
/// (И-49: тень с привязкой к loglig набрала 5 заявок). InMemory FK не проверяет — потому PG.
/// Заводит свои строки и удаляет их в finally; пропуск, если базы нет.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class SwimmerMergeEntriesPgTests
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
    public async Task Merge_MovesEntries_SameSlotKeepsCanonical()
    {
        await using var db = TryCreate();
        if (db == null) return; // PG недоступен — пропуск

        var clubId = await db.Clubs.Select(c => c.Id).FirstOrDefaultAsync();
        var styleId = await db.Styles.Select(s => s.Id).FirstOrDefaultAsync();
        if (clubId == 0 || styleId == 0) return; // пустая база — нечем заполнить FK

        var tag = $"pg-merge-{Guid.NewGuid():N}"[..20];
        var discipline = 900_000_000 + Random.Shared.Next(1, 99_999_999);
        var canon = new Swimmer { LastName = tag, FirstName = "canon", BirthYear = 2099 };
        var dup = new Swimmer { LastName = tag, FirstName = "dup", BirthYear = 2099 };
        db.Swimmers.AddRange(canon, dup);
        await db.SaveChangesAsync();
        try
        {
            // CompDate — календарная дата без пояса (timestamp without time zone).
            CompetitionEntry Entry(Swimmer s, int heat, int lane) => new()
            {
                OrgCompId = discipline, CompDate = new DateTime(2099, 1, 1), CompName = tag,
                SwimmerId = s.Id, ClubId = clubId, StyleId = styleId, Distance = "50", Gender = "male",
                OrgDisciplineId = discipline, Heat = heat, Lane = lane,
            };
            db.CompetitionEntries.AddRange(
                Entry(canon, heat: 1, lane: 4),
                Entry(dup, heat: 1, lane: 4),   // та же клетка — остаётся строка канонического
                Entry(dup, heat: 2, lane: 5));  // своя клетка — переезжает
            await db.SaveChangesAsync();

            var report = await new SwimmerMergeService(db)
                .MergeAsync([new SwimmerMergePair(canon.Id, dup.Id)], dryRun: false);

            Assert.Equal("merged", report.Pairs.Single().Status);

            await using var check = TryCreate()!;
            var entries = await check.CompetitionEntries.AsNoTracking()
                .Where(e => e.OrgDisciplineId == discipline)
                .OrderBy(e => e.Heat).Select(e => new { e.SwimmerId, e.Heat, e.Lane }).ToListAsync();
            Assert.Equal(new[] { (canon.Id, 1, 4), (canon.Id, 2, 5) },
                entries.Select(e => (e.SwimmerId, e.Heat, e.Lane)));
            Assert.False(await check.Swimmers.AnyAsync(s => s.Id == dup.Id));
        }
        finally
        {
            await using var cleanup = TryCreate()!;
            await cleanup.CompetitionEntries.Where(e => e.OrgDisciplineId == discipline).ExecuteDeleteAsync();
            await cleanup.Swimmers.Where(s => s.LastName == tag).ExecuteDeleteAsync();
        }
    }
}
