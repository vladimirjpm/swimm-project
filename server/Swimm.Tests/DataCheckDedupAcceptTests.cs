using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// «Принять» на находке-паре дедупа = «это не дубли, а тёзки».
///
/// Механизм обязан быть ОДИН: до 2026-08-23 их было два — ✕ на /Admin/Swimmers писала пару
/// в Sys_DedupIgnoredPairs, а «Принять» в реестре лишь помечала находку, и пара продолжала
/// висеть в списке дублей, снова прося склейки.
/// </summary>
public class DataCheckDedupAcceptTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static DataCheckFinding PairFinding(string checkId, string entityType, int canonId, int dupId) => new()
    {
        CheckId = checkId, Severity = 1, EntityType = entityType, EntityId = canonId,
        Message = $"#{canonId} ← #{dupId}",
        FixKind = DataCheckFixKinds.DedupIgnore, FixEntityId = dupId,
    };

    [Fact]
    public async Task Accept_WritesIgnoredPair_ForSwimmers()
    {
        await using var db = CreateDb(nameof(Accept_WritesIgnoredPair_ForSwimmers));
        db.DataCheckFindings.Add(PairFinding("swimmers.dedup-sure", "Swimmer", canonId: 7, dupId: 42));
        await db.SaveChangesAsync();
        var findingId = await db.DataCheckFindings.Select(f => f.Id).SingleAsync();

        var runner = new DataCheckRunner(db, [], dedupIgnore: new DedupIgnoreService(db));
        Assert.True(await runner.AcceptAsync(findingId, "тёзки"));

        var pair = await db.DedupIgnoredPairs.SingleAsync();
        Assert.Equal(DedupEntityType.Swimmer, pair.EntityType);
        Assert.Equal(7, pair.IdA);   // пара нормализована: меньший Id первым
        Assert.Equal(42, pair.IdB);
        Assert.Equal(DataCheckResolutions.Accepted, (await db.DataCheckFindings.SingleAsync()).Resolution);
    }

    [Fact]
    public async Task Accept_WritesIgnoredPair_ForClubs()
    {
        await using var db = CreateDb(nameof(Accept_WritesIgnoredPair_ForClubs));
        db.DataCheckFindings.Add(PairFinding("clubs.dedup-sure", "Club", canonId: 5, dupId: 3));
        await db.SaveChangesAsync();
        var findingId = await db.DataCheckFindings.Select(f => f.Id).SingleAsync();

        await new DataCheckRunner(db, [], dedupIgnore: new DedupIgnoreService(db))
            .AcceptAsync(findingId, null);

        var pair = await db.DedupIgnoredPairs.SingleAsync();
        Assert.Equal(DedupEntityType.Club, pair.EntityType);
        Assert.Equal(3, pair.IdA);
        Assert.Equal(5, pair.IdB);
    }

    [Fact]
    public async Task Accept_OnOrdinaryFinding_TouchesNoPairs()
    {
        await using var db = CreateDb(nameof(Accept_OnOrdinaryFinding_TouchesNoPairs));
        db.DataCheckFindings.Add(new DataCheckFinding
        {
            CheckId = "relays.gender-conflict", Severity = 1, EntityType = "Result", EntityId = 1,
            Message = "смешанный состав",
        });
        await db.SaveChangesAsync();
        var findingId = await db.DataCheckFindings.Select(f => f.Id).SingleAsync();

        await new DataCheckRunner(db, [], dedupIgnore: new DedupIgnoreService(db))
            .AcceptAsync(findingId, "решение Р16");

        Assert.Empty(await db.DedupIgnoredPairs.ToListAsync());
    }

    [Fact]
    public async Task TwoPairsWithSameCanonical_BothSurvive()
    {
        // Ключ находки включает второго участника: иначе «A ← B» и «A ← C» неразличимы,
        // вторая пара молча терялась при прогоне, а «принять» одну прятало обе.
        await using var db = CreateDb(nameof(TwoPairsWithSameCanonical_BothSurvive));
        var runner = new DataCheckRunner(db, [new TwoPairsCheck()]);

        await runner.RunAllAsync("test");

        var findings = await db.DataCheckFindings.OrderBy(f => f.FixEntityId).ToListAsync();
        Assert.Equal(2, findings.Count);
        Assert.Equal([42, 43], findings.Select(f => f.FixEntityId).ToArray());
    }

    /// <summary>Проверка-заглушка: две пары с ОДНИМ каноном.</summary>
    private sealed class TwoPairsCheck : Swimm.Application.Abstractions.IDataCheck
    {
        public string Id => "swimmers.dedup-sure";
        public string Title => "Дубли";
        public string Description => "";
        public DataCheckSeverity Severity => DataCheckSeverity.Warning;

        public Task<DataCheckOutcome> RunAsync(CancellationToken ct = default) =>
            Task.FromResult(new DataCheckOutcome(2, [
                new DataCheckItem("Swimmer", 7, "#7 ← #42",
                    FixKind: DataCheckFixKinds.DedupIgnore, FixEntityId: 42),
                new DataCheckItem("Swimmer", 7, "#7 ← #43",
                    FixKind: DataCheckFixKinds.DedupIgnore, FixEntityId: 43),
            ]));
    }
}
