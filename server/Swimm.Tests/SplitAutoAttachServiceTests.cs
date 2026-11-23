using Microsoft.EntityFrameworkCore;
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
/// Автодоклейка после импорта: какие дни, сколько прогонов, что ложится в аудит.
/// Правило самой доклейки — в <see cref="SplitAttachServiceTests"/>.
/// </summary>
public class SplitAutoAttachServiceTests
{
    private sealed record AuditRow(string Action, string EntityId, string Summary, object? Details);

    private static SwimmDbContext Db()
    {
        var db = new SwimmDbContext(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.CompetitionEvents.Add(new CompetitionEvent { Id = 7, Name = "чемпионат" });
        // Событие из двух дней одного compID + чужой старт.
        db.Competitions.Add(new Competition { Id = 1, EventId = 7, OrgCompId = 500, Name = "д1", Date = "01/02/2026", PoolType = "50m" });
        db.Competitions.Add(new Competition { Id = 2, EventId = 7, OrgCompId = 500, Name = "д2", Date = "02/02/2026", PoolType = "50m" });
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
                audit.Add(new AuditRow(action, id!, summary!, details)))
            .Returns(Task.CompletedTask);
        return (new SplitAutoAttachService(db, attach.Object, auditSvc.Object, NullLogger<SplitAutoAttachService>.Instance),
            attach, audit);
    }

    [Fact]
    public async Task OneRunPerEvent_ApplyTrue_AuditPerDay()
    {
        using var db = Db();
        var (service, attach, audit) = Build(db);
        attach.Setup(a => a.AttachAsync(1, null, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SplitAttachResult(true, 15896,
                [new SplitAttachDay(1, "д1", "01/02/2026", 8, 0), new SplitAttachDay(2, "д2", "02/02/2026", 0, 0)],
                new SplitAttachReport(2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0) { LegWrites = 8 }, "отчёт"));

        var outcome = await service.AttachAfterImportAsync(500, Guid.NewGuid());

        attach.Verify(a => a.AttachAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(SplitAutoKind.Attached, outcome.Kind);
        Assert.Equal(8, outcome.LegWrites);
        Assert.Equal([1, 2], outcome.CompetitionIds);
        Assert.Equal(["1", "2"], audit.Select(a => a.EntityId));
        Assert.All(audit, a => Assert.Equal(SplitAutoAttachRules.AuditAction, a.Action));
        Assert.Contains("доклеено: ног эстафет 8", audit[0].Summary);
        Assert.Contains("нового нет", audit[1].Summary);
    }

    // Сбой источника — «не удалось» дням этого compID в аудит, наружу не бросает.
    [Fact]
    public async Task Exception_BecomesFailed_WithAuditForTheDays()
    {
        using var db = Db();
        var (service, attach, audit) = Build(db);
        attach.Setup(a => a.AttachAsync(It.IsAny<int>(), null, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("loglig лёг"));

        var outcome = await service.AttachAfterImportAsync(500, Guid.NewGuid());

        Assert.Equal(SplitAutoKind.Failed, outcome.Kind);
        Assert.Contains("loglig лёг", outcome.Message);
        Assert.Equal(["1", "2"], audit.Select(a => a.EntityId));
        Assert.All(audit, a => Assert.Contains("не удалось", a.Summary));
    }

    [Fact]
    public async Task UnknownCompId_Failed_NoAttach()
    {
        using var db = Db();
        var (service, attach, audit) = Build(db);

        var outcome = await service.AttachAfterImportAsync(999, Guid.NewGuid());

        Assert.Equal(SplitAutoKind.Failed, outcome.Kind);
        attach.VerifyNoOtherCalls();
        Assert.Empty(audit);
    }
}
