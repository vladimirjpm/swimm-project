using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Перенос привязки к loglig при склейке против настоящего Postgres. Значение уходит с удаляемой
/// строки и встаёт на каноническую одним SaveChanges, а уникальный индекс <c>IX_Swimmers_LogligId</c>
/// проверяется на каждой команде — InMemory этого не видит (память «InMemory не видит CK Postgres»).
/// Заводит свои строки и удаляет их в finally; пропуск, если базы нет.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class SwimmerMergeLogligPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmDbContext? TryCreate()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql(Conn).Options);
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    [Theory]
    [InlineData(null)]        // у канонического привязки нет
    [InlineData("Rejected")]  // у канонического отклонённая метка — живая привязка дубля её вытесняет
    public async Task Merge_MovesLogligId_PassesUniqueIndex(string? canonicalStatus)
    {
        await using var db = TryCreate();
        if (db == null) return; // PG недоступен — пропуск

        var tag = $"pg-merge-{Guid.NewGuid():N}"[..20];
        var dupLoglig = 900_000_000 + Random.Shared.Next(1, 99_999_999);
        var canon = new Swimmer
        {
            LastName = tag, FirstName = "canon", BirthYear = 2099,
            LogligId = canonicalStatus == null ? null : dupLoglig + 1, LogligIdStatus = canonicalStatus,
        };
        var dup = new Swimmer
        {
            LastName = tag, FirstName = "dup", BirthYear = 2099,
            LogligId = dupLoglig, LogligIdStatus = "Verified", LogligIdSource = "auto",
            LogligIdVerifiedAt = DateTime.UtcNow,
        };
        db.Swimmers.AddRange(canon, dup);
        await db.SaveChangesAsync();
        try
        {
            var report = await new SwimmerMergeService(db)
                .MergeAsync([new SwimmerMergePair(canon.Id, dup.Id)], dryRun: false);

            Assert.Equal("merged", report.Pairs.Single().Status);

            await using var check = TryCreate()!;
            var after = await check.Swimmers.AsNoTracking().Where(s => s.LastName == tag).ToListAsync();
            var c = Assert.Single(after);
            Assert.Equal((canon.Id, dupLoglig, "Verified", "auto"), (c.Id, c.LogligId, c.LogligIdStatus, c.LogligIdSource));
        }
        finally
        {
            await using var cleanup = TryCreate()!;
            await cleanup.Swimmers.Where(s => s.LastName == tag).ExecuteDeleteAsync();
        }
    }
}
