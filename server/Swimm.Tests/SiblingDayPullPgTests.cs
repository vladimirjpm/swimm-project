using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
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

    /// <summary>
    /// Два сбора одних и тех же дней одновременно (кнопка + CLI, две вкладки): общий замок дня —
    /// второй ждёт, видит дни уже в событии и отказывает. Событие одно.
    /// </summary>
    [Fact]
    public async Task Join_TwoAtOnce_OneApplied_SecondRefused()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var tag = $"pg-sibj-{Guid.NewGuid():N}"[..18];
        var fri = new Competition { Name = $"{tag} שישי", Date = "06/12/2099", PoolType = "25m" };
        var sat = new Competition { Name = $"{tag} שבת", Date = "07/12/2099", PoolType = "25m" };
        db.Competitions.AddRange(fri, sat);
        await db.SaveChangesAsync();
        try
        {
            await using var db1 = TryCreate();
            await using var db2 = TryCreate();
            var results = await Task.WhenAll(
                new CompetitionDayJoinService(db1!, new NoRecalc()).JoinAsync([fri.Id, sat.Id], true, default, tag),
                new CompetitionDayJoinService(db2!, new NoRecalc()).JoinAsync([fri.Id, sat.Id], true, default, tag));

            Assert.Single(results, r => r.Applied);
            // Отказ либо до замка («уже день события»), либо под ним («уже попал в событие») — смотря кто раньше.
            Assert.Contains("событи", Assert.Single(results, r => !r.Applied).Refusal);
            Assert.Equal(1, await db.CompetitionEvents.CountAsync(e => e.Name == tag));
        }
        finally
        {
            db.ChangeTracker.Clear();
            var ev = await db.Competitions.Where(c => c.Id == fri.Id).Select(c => c.EventId).FirstOrDefaultAsync();
            await db.Competitions.Where(c => c.Id == fri.Id || c.Id == sat.Id).ExecuteDeleteAsync();
            await db.CompetitionEvents.Where(e => e.Name == tag).ExecuteDeleteAsync();
            _ = ev;
        }
    }

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    /// <summary>
    /// Насквозь на Postgres: недостающий день РАНЬШЕ лежащего, лежащий уже в событии. Запись (замок дня,
    /// запрос сверки заплывов на настоящей базе) → настоящий импорт в событие (имя события, уникальный
    /// ключ) → завершение перенумеровывает дни по дате: новый 06.12 — день 1, лежащий 07.12 — день 2.
    /// </summary>
    [Fact]
    public async Task ImportIntoExistingEvent_EarlierDay_RenumberedByDate()
    {
        await using var db = TryCreate();
        if (db == null) return;

        var tag = $"pg-sibe-{Guid.NewGuid():N}"[..18];
        var orgCompId = 900_000_000 + Random.Shared.Next(1, 99_999_999);
        var ev = new CompetitionEvent { Name = tag, OrgCompId = orgCompId };
        db.CompetitionEvents.Add(ev);
        await db.SaveChangesAsync();
        var sat = new Competition { Name = tag, SubName = $"{tag} יום שבת", Date = "07/12/2099", PoolType = "25m", EventId = ev.Id, DayNumber = 1 };
        var disc = new DiscoveredCompetition
        {
            OrgCompId = orgCompId, LogligId = 11588, Name = tag, Status = "imported",
            DateStart = new DateTime(2099, 12, 6, 0, 0, 0, DateTimeKind.Utc), DateEnd = new DateTime(2099, 12, 7, 0, 0, 0, DateTimeKind.Utc),
        };
        db.Competitions.Add(sat);
        db.DiscoveredCompetitions.Add(disc);
        await db.SaveChangesAsync();
        var swimmerId = await db.Swimmers.Select(s => s.Id).FirstAsync();
        var clubId = await db.Clubs.Select(c => c.Id).FirstAsync();
        var free = await db.Styles.FirstAsync(s => s.Name == "freestyle");
        db.Results.Add(new ResultRecord
        {
            CompetitionId = sat.Id, SwimmerId = swimmerId, ClubId = clubId, StyleId = free.Id, Distance = "100",
            Gender = "female", Heat = 1, Lane = 1, CompetitionDate = new DateTime(2099, 12, 7),
        });
        await db.SaveChangesAsync();

        var friTitle = $"{tag} יום שישי";
        var json = JsonSerializer.Serialize(Enumerable.Range(0, 3).Select(i => new
        {
            country = "ISR", competition = friTitle, date = "06/12/2099", event_style_name = "Freestyle", event_style_len = "50",
            event_style_gender = "male", pool_type = "25m", position = i + 1, heat = 1, lane = i + 1,
            last_name = $"{tag}{i}", first_name = "Tal", birth_year = 2012, club = tag, time = $"00:3{i}.00",
        }));
        var parsed = new ParsedCompetition
        {
            Format = "IsrOrg", ResultsJson = json, ResultCount = 3, Competitions = [new ParsedCompetitionSummary(friTitle, "06/12/2099", 3)],
        };
        var previewId = Guid.NewGuid();
        var previews = new Mock<IDiscoveryPreviewService>();
        previews.Setup(p => p.GetEntry(previewId)).Returns(new DiscoveryPreviewEntry(parsed, $"{tag}.pdf", disc.Id, null, SiblingLogligId: 11589));
        var loglig = new Mock<ICompetitionDiscoveryProvider>();
        loglig.Setup(l => l.FetchCompetitionSummaryAsync(11588, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogligCompetitionSummaryDto(11588, $"{tag} יום שבת 7.12.99 - 07/12/2099", [new DateOnly(2099, 12, 7)]));
        loglig.Setup(l => l.FetchCompetitionSummaryAsync(11589, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LogligCompetitionSummaryDto(11589, $"{tag} יום שישי 6.12.99 - 06/12/2099", [new DateOnly(2099, 12, 6)]));
        var queue = new BulkPullServiceTests.FakeQueue();
        SiblingDayPullService.ResetPendingForTests();
        var service = new SiblingDayPullService(db, Mock.Of<IProgrammeCheckService>(), loglig.Object, previews.Object, queue,
            new CompetitionDayJoinService(db, new NoRecalc()), Mock.Of<IAdminAuditService>(), NullLogger<SiblingDayPullService>.Instance);
        int? newDayId = null;
        try
        {
            var queued = await service.ImportAsync(disc.Id,
                new SiblingDayImportRequest(previewId, 11589, null, null, "25m", false, false, false, false, null), "utest");
            Assert.Null(queued.Refusal);
            var job = Assert.Single(queue.Jobs);
            Assert.Equal(ev.Id, job.Options!.EventId);

            db.ChangeTracker.Clear();
            var imported = await new JsonImportService(db, new NullCache())
                .ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(job.Payload)), job.FileName, null, job.Options);
            Assert.Equal(3, imported.Created);
            db.ChangeTracker.Clear();
            var done = await service.CompleteAsync(job.Options.SiblingDay!, imported);
            newDayId = done.CompetitionId;

            Assert.True(done.Joined, done.Message);
            Assert.Equal("Готово. Теперь у соревнования два дня: 06.12 (3 строки) и 07.12 (1 строка).", done.Message);
            db.ChangeTracker.Clear();
            var days = await db.Competitions.AsNoTracking().Where(c => c.EventId == ev.Id).OrderBy(c => c.DayNumber).ToListAsync();
            Assert.Equal([newDayId!.Value, sat.Id], days.Select(d => d.Id));
            Assert.Equal([1, 2], days.Select(d => d.DayNumber ?? 0));
            Assert.All(days, d => Assert.Equal(tag, d.Name));
            Assert.Equal(friTitle, days[0].SubName);
        }
        finally
        {
            db.ChangeTracker.Clear();
            var ids = new[] { sat.Id, newDayId ?? -1 };
            await db.Results.Where(r => ids.Contains(r.CompetitionId)).ExecuteDeleteAsync();
            await db.ImportHistory.Where(h => ids.Contains(h.CompetitionId)).ExecuteDeleteAsync();
            await db.CompetitionSources.Where(s => ids.Contains(s.CompetitionId)).ExecuteDeleteAsync();
            await db.CategoryCompetitions.Where(c => ids.Contains(c.CompetitionId)).ExecuteDeleteAsync();
            await db.Competitions.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync();
            await db.CompetitionEvents.Where(e => e.Id == ev.Id).ExecuteDeleteAsync();
            await db.DiscoveredCompetitions.Where(d => d.Id == disc.Id).ExecuteDeleteAsync();
            await db.Swimmers.Where(s => s.LastName.StartsWith(tag)).ExecuteDeleteAsync();
            await db.Clubs.Where(c => c.Name == tag).ExecuteDeleteAsync();
        }
    }
}
