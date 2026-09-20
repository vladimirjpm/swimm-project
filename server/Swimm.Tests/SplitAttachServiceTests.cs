using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Доклейка промежуточных без переимпорта, сервисный слой
/// (docs/plans/splits-attach-without-repull-plan.md Д2).
///
/// Здесь проверяется ровно то, чего не видит чистый матчер: сухой прогон ничего не пишет,
/// боевой пишет ДВА поля и ставит HasSplits, а прогон по одному дню многодневки забирает
/// строки всех дней события (решение Влада §6-2).
/// </summary>
public class SplitAttachServiceTests
{
    private sealed class SourceStub(SplitSource source) : ISplitSourceProvider
    {
        public Task<SplitSource> FetchAsync(int logligId, CancellationToken ct = default) =>
            Task.FromResult(source);
    }

    private static SwimmDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>
    /// Двухдневный старт: день 1 — эстафета 4×50 вольным (четыре ноги без промежуточных),
    /// день 2 — личные 100 на спине. Оба дня под одним событием, строка discovery одна.
    /// </summary>
    private static async Task<SwimmDbContext> WorldAsync()
    {
        var db = CreateDb();
        db.CompetitionEvents.Add(new CompetitionEvent { Id = 7, Name = "чемпионат" });
        db.Competitions.Add(new Competition
        {
            Id = 1, EventId = 7, OrgCompId = 500, Name = "день 1", Date = "01/02/2026", PoolType = "25m",
        });
        db.Competitions.Add(new Competition
        {
            Id = 2, EventId = 7, OrgCompId = 501, Name = "день 2", Date = "02/02/2026", PoolType = "25m",
        });
        db.DiscoveredCompetitions.Add(new DiscoveredCompetition
        {
            Id = 9, OrgCompId = 500, LogligId = 12345, Name = "чемпионат", DateStart = new DateTime(2026, 2, 1),
        });
        db.Styles.Add(new Style { Id = 1, Name = "freestyle" });
        db.Styles.Add(new Style { Id = 2, Name = "backstroke" });
        db.Relays.Add(new Relay { Id = 50, TeamName = "клуб" });

        for (var i = 1; i <= 4; i++)
        {
            db.Swimmers.Add(new Swimmer { Id = i, FirstName = $"нога{i}", LastName = "п", BirthYear = 2010 + (i % 2) });
            db.RelayMembers.Add(new RelayMember { Id = i * 10, RelayId = 50, SwimmerId = i, LegOrder = i });
        }

        db.Swimmers.Add(new Swimmer { Id = 9, FirstName = "личник", LastName = "п", BirthYear = 1981 });

        db.Results.Add(new ResultRecord
        {
            Id = 1000, CompetitionId = 1, SwimmerId = 1, StyleId = 1, RelayId = 50,
            CompetitionDate = new DateTime(2026, 2, 1), Distance = "4X50", Gender = "male",
            Heat = 3, Lane = 4, TimeOriginal = "01:52.30",
        });
        db.Results.Add(new ResultRecord
        {
            Id = 1001, CompetitionId = 2, SwimmerId = 9, StyleId = 2,
            CompetitionDate = new DateTime(2026, 2, 2), Distance = "100", Gender = "female",
            Heat = 1, Lane = 5, TimeOriginal = "01:06.36",
        });

        await db.SaveChangesAsync();
        return db;
    }

    private static SplitSource Source() => new(
        [new SplitSourceTeam("freestyle", "4X50", 3, 4, "01:52.30",
        [
            new SplitSourceLeg(1, 2011, "00:28.10"), new SplitSourceLeg(2, 2010, "00:28.40"),
            new SplitSourceLeg(3, 2011, "00:27.90"), new SplitSourceLeg(4, 2010, "00:27.90"),
        ])],
        [new SplitSourceSwim("backstroke", "100", "female", "01:06.36", 1981, ["31.52", "34.84"])],
        "источник-заглушка");

    private static SplitAttachService Service(SwimmDbContext db) =>
        new(db, new SourceStub(Source()), NullLogger<SplitAttachService>.Instance);

    [Fact]
    public async Task DryRun_WritesNothing()
    {
        await using var db = await WorldAsync();

        var result = await Service(db).AttachAsync(1, logligId: null, apply: false);

        Assert.False(result.Applied);
        Assert.Equal(1, result.Report.TeamsAttached);
        Assert.Equal(1, result.Report.SwimsAttached);
        Assert.Contains("СУХОЙ ПРОГОН", result.Message);
        Assert.All(await db.RelayMembers.ToListAsync(), m => Assert.Null(m.SplitTime));
        Assert.All(await db.Competitions.ToListAsync(), c => Assert.False(c.HasSplits));
    }

    [Fact]
    public async Task Apply_WritesSplitsAndHasSplits_ForAllDaysOfEvent()
    {
        await using var db = await WorldAsync();

        // Прогон запущен по ДНЮ 1, а личный заплыв лежит в дне 2: строки берутся по всему
        // событию, иначе день молча остался бы без промежуточных.
        var result = await Service(db).AttachAsync(1, logligId: null, apply: true);

        Assert.True(result.Applied);
        Assert.Equal(12345, result.LogligId);
        Assert.Equal("00:28.10", (await db.RelayMembers.SingleAsync(m => m.LegOrder == 1)).SplitTime);
        Assert.Equal("00:27.90", (await db.RelayMembers.SingleAsync(m => m.LegOrder == 4)).SplitTime);
        Assert.Equal("31.52;34.84", (await db.Results.SingleAsync(r => r.Id == 1001)).TimeSplit);
        Assert.True((await db.Competitions.SingleAsync(c => c.Id == 1)).HasSplits);
        Assert.True((await db.Competitions.SingleAsync(c => c.Id == 2)).HasSplits);

        var day1 = result.Days.Single(d => d.CompetitionId == 1);
        var day2 = result.Days.Single(d => d.CompetitionId == 2);
        Assert.Equal(4, day1.LegWrites);
        Assert.Equal(0, day1.SwimWrites);
        Assert.Equal(1, day2.SwimWrites);
    }

    [Fact]
    public async Task SecondRun_ChangesNothing()
    {
        await using var db = await WorldAsync();
        var service = Service(db);
        await service.AttachAsync(1, logligId: null, apply: true);

        var second = await service.AttachAsync(1, logligId: null, apply: true);

        Assert.Equal(0, second.Report.LegWrites);
        Assert.Equal(0, second.Report.SwimWrites);
        Assert.Equal(1, second.Report.TeamsAttached);
    }

    [Fact]
    public async Task NoDiscoveryRow_AsksForLogligId()
    {
        await using var db = await WorldAsync();
        db.DiscoveredCompetitions.RemoveRange(await db.DiscoveredCompetitions.ToListAsync());
        await db.SaveChangesAsync();

        var result = await Service(db).AttachAsync(1, logligId: null, apply: true);

        Assert.Contains("LogligId не найден", result.Message);
        Assert.All(await db.RelayMembers.ToListAsync(), m => Assert.Null(m.SplitTime));
    }

    [Fact]
    public async Task NoDiscoveryRow_ExplicitLogligId_Works()
    {
        await using var db = await WorldAsync();
        db.DiscoveredCompetitions.RemoveRange(await db.DiscoveredCompetitions.ToListAsync());
        await db.SaveChangesAsync();

        var result = await Service(db).AttachAsync(1, logligId: 777, apply: true);

        Assert.Equal(777, result.LogligId);
        Assert.Equal(4, result.Report.LegWrites);
    }

    [Fact]
    public async Task UnknownCompetition_Fails()
    {
        await using var db = await WorldAsync();

        var result = await Service(db).AttachAsync(404, logligId: null, apply: true);

        Assert.Contains("нет", result.Message);
        Assert.False(result.Applied);
    }
}
