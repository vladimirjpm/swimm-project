using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>Перенос результатов между соревнованиями (ResultTransferService): dry-run, apply, дубли.</summary>
public class ResultTransferServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    private static ResultTransferService Svc(SwimmDbContext db) => new(db, new NullCache());

    private static async Task<(Competition src, Competition dst, Swimmer sw, Style st, Club club)> Seed(SwimmDbContext db)
    {
        var src = new Competition { Name = "Day 1", Date = "01/06/2026", PoolType = "25m" };
        var dst = new Competition { Name = "Day 2", Date = "02/06/2026", PoolType = "25m" };
        var sw = new Swimmer { LastName = "A", FirstName = "X" };
        var st = new Style { Name = "freestyle" };
        var club = new Club { Name = "Club" };
        db.AddRange(src, dst, sw, st, club);
        await db.SaveChangesAsync();
        return (src, dst, sw, st, club);
    }

    private static ResultRecord Res(Competition c, Swimmer sw, Style st, Club club, string dist) => new()
    {
        Competition = c, Swimmer = sw, Style = st, Club = club, Distance = dist, Gender = "male",
        CompetitionDate = new DateTime(2026, 6, 1)
    };

    [Fact]
    public async Task Apply_MovesAllResults_AndUpdatesDate()
    {
        await using var db = CreateDb(nameof(Apply_MovesAllResults_AndUpdatesDate));
        var (src, dst, sw, st, club) = await Seed(db);
        db.Results.Add(Res(src, sw, st, club, "50"));
        db.Results.Add(Res(src, sw, st, club, "100"));
        await db.SaveChangesAsync();

        var report = await Svc(db).MoveResultsAsync(src.Id, dst.Id, apply: true);

        Assert.True(report.Applied);
        Assert.Equal(2, report.ResultsToMove);
        Assert.Equal(0, await db.Results.CountAsync(r => r.CompetitionId == src.Id));
        Assert.Equal(2, await db.Results.CountAsync(r => r.CompetitionId == dst.Id));
        Assert.All(await db.Results.ToListAsync(),
            r => Assert.Equal(new DateTime(2026, 6, 2), r.CompetitionDate));   // дата цели
    }

    [Fact]
    public async Task DryRun_ReportsCounts_ChangesNothing()
    {
        await using var db = CreateDb(nameof(DryRun_ReportsCounts_ChangesNothing));
        var (src, dst, sw, st, club) = await Seed(db);
        db.Results.Add(Res(src, sw, st, club, "50"));
        await db.SaveChangesAsync();

        var report = await Svc(db).MoveResultsAsync(src.Id, dst.Id, apply: false);

        Assert.False(report.Applied);
        Assert.Equal(1, report.ResultsToMove);
        Assert.Equal(1, await db.Results.CountAsync(r => r.CompetitionId == src.Id));   // не тронуто
    }

    [Fact]
    public async Task Overlap_CountsSharedIndividualSwims()
    {
        await using var db = CreateDb(nameof(Overlap_CountsSharedIndividualSwims));
        var (src, dst, sw, st, club) = await Seed(db);
        db.Results.Add(Res(src, sw, st, club, "50"));   // совпадает по (пловец,стиль,дист) с целью
        db.Results.Add(Res(src, sw, st, club, "200"));  // уникально
        db.Results.Add(Res(dst, sw, st, club, "50"));
        await db.SaveChangesAsync();

        var report = await Svc(db).MoveResultsAsync(src.Id, dst.Id, apply: false);
        Assert.Equal(1, report.OverlapCount);
    }

    [Fact]
    public async Task SameCompetition_Throws()
    {
        await using var db = CreateDb(nameof(SameCompetition_Throws));
        var (src, _, _, _, _) = await Seed(db);
        await Assert.ThrowsAsync<ArgumentException>(() => Svc(db).MoveResultsAsync(src.Id, src.Id, apply: true));
    }

    [Fact]
    public async Task MissingCompetition_Throws()
    {
        await using var db = CreateDb(nameof(MissingCompetition_Throws));
        var (src, _, _, _, _) = await Seed(db);
        await Assert.ThrowsAsync<ArgumentException>(() => Svc(db).MoveResultsAsync(src.Id, 999999, apply: true));
    }
}
