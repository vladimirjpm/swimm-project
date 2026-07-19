using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// «Развязка» пар дедупа (Sys_DedupIgnoredPairs): нормализация порядка Id,
/// идемпотентность, удаление, и главное — скрытая пара не всплывает в кандидатах
/// ни у пловцов, ни у клубов.
/// </summary>
public class DedupIgnoreServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Fact]
    public async Task Add_NormalizesOrder_AndIsIdempotent()
    {
        await using var db = CreateDb(nameof(Add_NormalizesOrder_AndIsIdempotent));
        var svc = new DedupIgnoreService(db);

        await svc.AddAsync(DedupEntityType.Swimmer, 20, 10);
        await svc.AddAsync(DedupEntityType.Swimmer, 10, 20);   // тот же в другом порядке

        var row = await db.DedupIgnoredPairs.SingleAsync();    // одна строка
        Assert.Equal(10, row.IdA);
        Assert.Equal(20, row.IdB);
    }

    [Fact]
    public async Task Remove_UnknownPair_ReturnsFalse()
    {
        await using var db = CreateDb(nameof(Remove_UnknownPair_ReturnsFalse));
        var svc = new DedupIgnoreService(db);

        Assert.False(await svc.RemoveAsync(DedupEntityType.Club, 1, 2));

        await svc.AddAsync(DedupEntityType.Club, 1, 2);
        Assert.True(await svc.RemoveAsync(DedupEntityType.Club, 2, 1)); // порядок не важен
        Assert.Empty(await db.DedupIgnoredPairs.ToListAsync());
    }

    [Fact]
    public async Task SwimmerCandidates_IgnoredPair_NotReported()
    {
        await using var db = CreateDb(nameof(SwimmerCandidates_IgnoredPair_NotReported));
        // Левенштейн-пара (RAVIV/HAVIV — реальный кейс однофамильцев из одного заплыва).
        var a = new Swimmer { LastName = "רביב", FirstName = "יונתן", BirthYear = 2015, Gender = "male" };
        var b = new Swimmer { LastName = "חביב", FirstName = "יונתן", BirthYear = 2015, Gender = "male" };
        db.AddRange(a, b);
        await db.SaveChangesAsync();

        var before = await new SwimmerDedupService(db).FindCandidatesAsync();
        Assert.Single(before.Candidates);                       // без развязки пара видна

        await new DedupIgnoreService(db).AddAsync(DedupEntityType.Swimmer, b.Id, a.Id);

        var after = await new SwimmerDedupService(db).FindCandidatesAsync();
        Assert.Empty(after.Candidates);                         // развязана — не всплывает
    }

    [Fact]
    public async Task ClubCandidates_IgnoredPair_NotReported()
    {
        await using var db = CreateDb(nameof(ClubCandidates_IgnoredPair_NotReported));
        var a = new Club { Name = "Maccabi Haifa" };
        var b = new Club { Name = "Macabi Haifa" };
        db.AddRange(a, b);
        await db.SaveChangesAsync();

        var before = await new ClubDedupService(db).FindCandidatesAsync();
        Assert.Single(before.Candidates);

        await new DedupIgnoreService(db).AddAsync(DedupEntityType.Club, a.Id, b.Id);

        var after = await new ClubDedupService(db).FindCandidatesAsync();
        Assert.Empty(after.Candidates);
    }

    [Fact]
    public async Task List_ResolvesNames_ByEntityType()
    {
        await using var db = CreateDb(nameof(List_ResolvesNames_ByEntityType));
        var s1 = new Swimmer { LastName = "רביב", FirstName = "יונתן", BirthYear = 2015 };
        var s2 = new Swimmer { LastName = "חביב", FirstName = "יונתן", BirthYear = 2015 };
        db.AddRange(s1, s2);
        await db.SaveChangesAsync();

        var svc = new DedupIgnoreService(db);
        await svc.AddAsync(DedupEntityType.Swimmer, s2.Id, s1.Id);

        var list = await svc.ListAsync(DedupEntityType.Swimmer);
        var p = Assert.Single(list);
        Assert.Equal(Math.Min(s1.Id, s2.Id), p.IdA);
        Assert.Contains("יונתן", p.NameA);

        Assert.Empty(await svc.ListAsync(DedupEntityType.Club)); // тип не смешивается
    }
}
