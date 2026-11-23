using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Боевые прогоны доклейки с итогом в аудите: сама после импорта и кнопкой. Какие дни, сколько
/// прогонов, что ложится в аудит и что остаётся в базе после сбоя посреди записи.
/// Правило самой доклейки — в <see cref="SplitAttachServiceTests"/>.
/// </summary>
public class SplitAutoAttachServiceTests
{
    private sealed record AuditRow(string Action, string EntityId, string Summary, string DetailsJson);

    private static SwimmDbContext Db()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.CompetitionEvents.Add(new CompetitionEvent { Id = 7, Name = "чемпионат" });
        // Событие: два дня compID 500 и третий — другого compID 501 (как окружные e1/e7/e10/e12).
        db.Competitions.Add(new Competition { Id = 1, EventId = 7, OrgCompId = 500, Name = "д1", Date = "01/02/2026", PoolType = "50m" });
        db.Competitions.Add(new Competition { Id = 2, EventId = 7, OrgCompId = 500, Name = "д2", Date = "02/02/2026", PoolType = "50m" });
        db.Competitions.Add(new Competition { Id = 4, EventId = 7, OrgCompId = 501, Name = "д3", Date = "03/02/2026", PoolType = "50m" });
        db.Competitions.Add(new Competition { Id = 3, OrgCompId = 600, Name = "другой", Date = "02/02/2026", PoolType = "25m" });
        db.SaveChanges();
        return db;
    }

    private static (SplitAutoAttachService Service, Mock<ISplitAttachService> Attach, List<AuditRow> Audit) Build(SwimmDbContext db)
    {
        var attach = new Mock<ISplitAttachService>();
        var audit = new List<AuditRow>();
        var auditSvc = new Mock<IAdminAuditService>();
        auditSvc.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback((string action, string _, string? id, string? summary, object? details, CancellationToken _) =>
                audit.Add(new AuditRow(action, id!, summary!, JsonSerializer.Serialize(details))))
            .Returns(Task.CompletedTask);
        return (new SplitAutoAttachService(db, attach.Object, auditSvc.Object, NullLogger<SplitAutoAttachService>.Instance),
            attach, audit);
    }

    private static SplitAttachResult Result(int day1Legs, int day2Legs = 0, bool incomplete = false) =>
        new(true, 15896,
            [new SplitAttachDay(1, "д1", "01/02/2026", day1Legs, 0), new SplitAttachDay(2, "д2", "02/02/2026", day2Legs, 0)],
            new SplitAttachReport(2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0) { LegWrites = day1Legs + day2Legs }, "отчёт")
        { SourceIncomplete = incomplete };

    private static string Kind(AuditRow row) =>
        JsonDocument.Parse(row.DetailsJson).RootElement.GetProperty("Kind").GetString()!;

    [Fact]
    public async Task AfterImport_OneRunPerEvent_OnlyDaysOfThisCompId_AuditPerDay()
    {
        using var db = Db();
        var (service, attach, audit) = Build(db);
        IReadOnlyCollection<int>? scope = null;
        attach.Setup(a => a.AttachAsync(1, null, true, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .Callback((int _, int? _, bool _, CancellationToken _, IReadOnlyCollection<int>? only) => scope = only)
            .ReturnsAsync(Result(day1Legs: 8));

        var outcome = await service.AttachAfterImportAsync(500, Guid.NewGuid());

        attach.Verify(a => a.AttachAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(),
            It.IsAny<IReadOnlyCollection<int>?>()), Times.Once);
        // День 3 (compID 501) в прогон не попал: у него свой турнир loglig.
        Assert.Equal([1, 2], scope!.OrderBy(x => x));
        Assert.Equal(SplitAutoKind.Attached, outcome.Kind);
        Assert.Equal(8, outcome.LegWrites);
        Assert.Equal(["1", "2"], audit.Select(a => a.EntityId));
        Assert.All(audit, a => Assert.Equal(SplitAutoAttachRules.AuditAction, a.Action));
        Assert.Equal(["attached", "nothing-new"], audit.Select(Kind));
        Assert.All(audit, a => Assert.Contains("\"Trigger\":\"import\"", a.DetailsJson));
    }

    // Эстафеты легли, личные упали на 504 — не «доклеено», а «частично», у всех дней прогона.
    [Fact]
    public async Task AfterImport_IncompleteSourceWithWrites_IsPartial()
    {
        using var db = Db();
        var (service, attach, audit) = Build(db);
        attach.Setup(a => a.AttachAsync(1, null, true, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .ReturnsAsync(Result(day1Legs: 8, incomplete: true));

        var outcome = await service.AttachAfterImportAsync(500, Guid.NewGuid());

        Assert.Equal(SplitAutoKind.Partial, outcome.Kind);
        Assert.Equal(["partial", "partial"], audit.Select(Kind));
        Assert.Contains("частично", audit[0].Summary);
    }

    // Сбой источника — «не удалось» дням этого compID в аудит, наружу не бросает.
    [Fact]
    public async Task AfterImport_Exception_BecomesFailed_WithAuditForTheDays()
    {
        using var db = Db();
        var (service, attach, audit) = Build(db);
        attach.Setup(a => a.AttachAsync(It.IsAny<int>(), null, true, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyCollection<int>?>()))
            .ThrowsAsync(new HttpRequestException("loglig лёг"));

        var outcome = await service.AttachAfterImportAsync(500, Guid.NewGuid());

        Assert.Equal(SplitAutoKind.Failed, outcome.Kind);
        Assert.Contains("loglig лёг", outcome.Message);
        Assert.Equal(["1", "2"], audit.Select(a => a.EntityId));
        Assert.All(audit, a => Assert.Contains("не удалось", a.Summary));
    }

    [Fact]
    public async Task AfterImport_UnknownCompId_Failed_NoAttach()
    {
        using var db = Db();
        var (service, attach, audit) = Build(db);

        var outcome = await service.AttachAfterImportAsync(999, Guid.NewGuid());

        Assert.Equal(SplitAutoKind.Failed, outcome.Kind);
        attach.VerifyNoOtherCalls();
        Assert.Empty(audit);
    }

    // Кнопка: итог пишется ВСЕГДА, и «нечего» тоже — иначе шаг 9 остался бы на прежнем «не удалось».
    [Fact]
    public async Task Button_NothingNew_StillRecordedForEveryDayOfTheRun()
    {
        using var db = Db();
        var (service, attach, audit) = Build(db);
        attach.Setup(a => a.AttachAsync(2, 777, true, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(Result(day1Legs: 0));

        var run = await service.AttachByButtonAsync(2, 777);

        Assert.False(run.Threw);
        Assert.Equal(SplitAutoKind.NothingNew, run.Kind);
        Assert.Equal(["1", "2"], audit.Select(a => a.EntityId));
        Assert.Equal(["nothing-new", "nothing-new"], audit.Select(Kind));
        Assert.All(audit, a => Assert.Contains("\"Trigger\":\"button\"", a.DetailsJson));
        Assert.All(audit, a => Assert.Contains("кнопкой", a.Summary));
    }

    // Сбой кнопки — отчёта по дням нет: «не удалось» всем дням события, ok:false панели.
    [Fact]
    public async Task Button_Exception_RecordsFailedForAllEventDays()
    {
        using var db = Db();
        var (service, attach, audit) = Build(db);
        attach.Setup(a => a.AttachAsync(1, null, true, It.IsAny<CancellationToken>(), null))
            .ThrowsAsync(new TaskCanceledException("HttpClient.Timeout"));

        var run = await service.AttachByButtonAsync(1, null);

        Assert.True(run.Threw);
        Assert.Contains("таймаут запроса", run.Result.Message);
        Assert.Equal(["1", "2", "4"], audit.Select(a => a.EntityId).OrderBy(x => x));
        Assert.All(audit, a => Assert.Equal("failed", Kind(a)));
    }

    [Fact]
    public void FailureText_TellsRequestTimeoutFromCeiling()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Contains("потолка", SplitAutoAttachService.FailureText(new OperationCanceledException(), cts.Token));
        Assert.Contains("таймаут запроса", SplitAutoAttachService.FailureText(new TaskCanceledException(), CancellationToken.None));
        Assert.StartsWith("не удалось", SplitAutoAttachService.FailureText(new HttpRequestException("x"), CancellationToken.None));
    }

    /// <summary>Роняет ПЕРВОЕ сохранение, где меняются ноги эстафет, — «база упала посреди записи».</summary>
    private sealed class FailFirstLegWrite : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<RelayMember>().Any(e => e.State == EntityState.Modified))
            {
                Armed = false;
                throw new DbUpdateException("база упала посреди записи");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    // Ревью PR #273, находка 1. Настоящие доклейка и аудит на одном контексте: запись промежуточных
    // падает — аудит «не удалось» записан, а в базе ни одной ноги, ни строки, ни HasSplits.
    // Без очистки трекера сохранение аудита дописало бы ноги и флаг дня.
    [Fact]
    public async Task WriteFailure_LeavesDatabaseUntouched_AndRecordsFailed()
    {
        var fail = new FailFirstLegWrite();
        await using var db = await SplitAttachServiceTests.WorldAsync(fail);
        var attach = new SplitAttachService(db, new SplitAttachServiceTests.SourceStub(SplitAttachServiceTests.Source()),
            NullLogger<SplitAttachService>.Instance);
        var actor = Mock.Of<ICurrentActor>(a => a.Name == SplitAutoAttachRules.ActorName);
        var audit = new AdminAuditService(db, actor, NullLogger<AdminAuditService>.Instance);
        var service = new SplitAutoAttachService(db, attach, audit, NullLogger<SplitAutoAttachService>.Instance);
        fail.Armed = true;

        var outcome = await service.AttachAfterImportAsync(500, Guid.NewGuid());

        Assert.Equal(SplitAutoKind.Failed, outcome.Kind);
        db.ChangeTracker.Clear();
        Assert.All(await db.RelayMembers.ToListAsync(), m => Assert.Null(m.SplitTime));
        Assert.True(string.IsNullOrEmpty((await db.Results.SingleAsync(r => r.Id == 1001)).TimeSplit));
        Assert.All(await db.Competitions.ToListAsync(), c => Assert.False(c.HasSplits));
        var row = await db.AdminAudits.SingleAsync(a => a.Action == SplitAutoAttachRules.AuditAction);
        Assert.Equal(SplitAutoAttachRules.ActorName, row.ActorName);
        Assert.Contains("\"Kind\":\"failed\"", row.DetailsJson);
        Assert.Contains("база упала", JsonDocument.Parse(row.DetailsJson!).RootElement.GetProperty("Message").GetString());
    }
}
