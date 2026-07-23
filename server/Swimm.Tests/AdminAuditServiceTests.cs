using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Аудит ручных мутаций (фаза 7.4): запись снимает актора из <see cref="ICurrentActor"/>,
/// сериализует детали в JSON, режет слишком длинные поля; чтение фильтрует/пагинирует.
/// </summary>
public class AdminAuditServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed record FakeActor(int? UserId, string Name, string? IpAddress) : ICurrentActor;

    private static AdminAuditService Svc(SwimmDbContext db, ICurrentActor actor) =>
        new(db, actor, NullLogger<AdminAuditService>.Instance);

    [Fact]
    public async Task LogAsync_WritesActorSnapshot_AndSerializesDetails()
    {
        await using var db = CreateDb(nameof(LogAsync_WritesActorSnapshot_AndSerializesDetails));
        var svc = Svc(db, new FakeActor(42, "admin@example.com", "10.0.0.1"));

        await svc.LogAsync("swimmer.merge", "Swimmer", "7",
            "Склейка пловцов", new { canonical = 7, duplicate = 9 });

        var row = await db.AdminAudits.SingleAsync();
        Assert.Equal(42, row.ActorUserId);
        Assert.Equal("admin@example.com", row.ActorName);
        Assert.Equal("10.0.0.1", row.IpAddress);
        Assert.Equal("swimmer.merge", row.Action);
        Assert.Equal("Swimmer", row.EntityType);
        Assert.Equal("7", row.EntityId);
        Assert.Equal("Склейка пловцов", row.Summary);
        Assert.NotNull(row.DetailsJson);
        Assert.Contains("\"canonical\":7", row.DetailsJson);
        Assert.True(row.CreatedAt.Kind == DateTimeKind.Utc);
    }

    [Fact]
    public async Task LogAsync_NoDetails_LeavesJsonNull()
    {
        await using var db = CreateDb(nameof(LogAsync_NoDetails_LeavesJsonNull));
        var svc = Svc(db, new FakeActor(null, "cli", null));

        await svc.LogAsync("setting.update", "Setting", "MaintenanceMode", "включён");

        var row = await db.AdminAudits.SingleAsync();
        Assert.Null(row.ActorUserId);
        Assert.Equal("cli", row.ActorName);
        Assert.Null(row.DetailsJson);
    }

    [Fact]
    public async Task LogAsync_TruncatesOverlongFields()
    {
        await using var db = CreateDb(nameof(LogAsync_TruncatesOverlongFields));
        var svc = Svc(db, new FakeActor(1, new string('n', 500), null));

        await svc.LogAsync("x", "Y", null, new string('s', 5000));

        var row = await db.AdminAudits.SingleAsync();
        Assert.Equal(256, row.ActorName.Length);
        Assert.Equal(1000, row.Summary.Length);
    }

    [Fact]
    public async Task Query_FiltersByAction_AndOrdersNewestFirst()
    {
        await using var db = CreateDb(nameof(Query_FiltersByAction_AndOrdersNewestFirst));
        var svc = Svc(db, new FakeActor(1, "a@b.c", null));
        await svc.LogAsync("club.merge", "Club", "1", "первый");
        await svc.LogAsync("swimmer.merge", "Swimmer", "2", "второй");
        await svc.LogAsync("club.merge", "Club", "3", "третий");

        var repo = new AdminAuditRepository(db);
        var page = await repo.QueryAsync(new AdminAuditFilter(Action: "club.merge"));

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("третий", page.Items[0].Summary);   // новее — сверху (по Id desc)
        Assert.Equal("первый", page.Items[1].Summary);
    }

    [Fact]
    public async Task Query_FiltersBySinceUtc_ExcludesOlderRows()
    {
        await using var db = CreateDb(nameof(Query_FiltersBySinceUtc_ExcludesOlderRows));
        var now = DateTime.UtcNow;
        db.AdminAudits.AddRange(
            new Swimm.Domain.Entities.AdminAudit { Action = "x", EntityType = "Y", Summary = "старая", CreatedAt = now.AddDays(-10) },
            new Swimm.Domain.Entities.AdminAudit { Action = "x", EntityType = "Y", Summary = "недавняя", CreatedAt = now.AddHours(-1) });
        await db.SaveChangesAsync();

        var repo = new AdminAuditRepository(db);
        var page = await repo.QueryAsync(new AdminAuditFilter(SinceUtc: now.AddDays(-1)));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("недавняя", page.Items[0].Summary);
    }

    [Fact]
    public async Task GetDistinctActions_ReturnsSortedUnique()
    {
        await using var db = CreateDb(nameof(GetDistinctActions_ReturnsSortedUnique));
        var svc = Svc(db, new FakeActor(1, "a@b.c", null));
        await svc.LogAsync("swimmer.merge", "Swimmer", "1", "s");
        await svc.LogAsync("club.merge", "Club", "2", "c");
        await svc.LogAsync("club.merge", "Club", "3", "c");

        var repo = new AdminAuditRepository(db);
        var actions = await repo.GetDistinctActionsAsync();

        Assert.Equal(new[] { "club.merge", "swimmer.merge" }, actions);
    }
}
