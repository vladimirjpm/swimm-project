using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Уровни и планы дорожек против НАСТОЯЩЕГО Postgres (docs/plans/lane-plans-plan.md): то, чего
/// InMemory не видит — advisory-lock с транзакцией внутри retry-стратегии, check-ограничения,
/// составной FK уровня, каскады и порядок команд EF при склейке пловцов (строка с SwimmerId в
/// ключе удаляется и вставляется заново, дубль удаляется следом). Пропуск, если PG недоступен.
///
/// Тест ходит в рабочую локальную базу: заводит свою группу и двух своих пловцов и в finally
/// удаляет их — каскад уносит уровни и планы.
/// </summary>
[Collection(LiveDbCollection.Name)]
public class LanePlanPgTests
{
    private const string Conn =
        "Host=localhost;Port=5445;Database=swimm;Username=swimm;Password=swimm_local_dev";

    private static SwimmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseNpgsql(Conn, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3))
            .Options);

    private static SwimmDbContext? TryCreate()
    {
        var db = NewContext();
        try { if (!db.Database.CanConnect()) { db.Dispose(); return null; } return db; }
        catch { db.Dispose(); return null; }
    }

    [Fact]
    public async Task PlanSaveShrink_ThenSwimmerMerge_OnPostgres()
    {
        await using var probe = TryCreate();
        if (probe == null) return; // PG недоступен — пропуск
        var ownerId = await probe.AppUsers.OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync();
        if (ownerId == null) return;

        var tag = Guid.NewGuid().ToString("N")[..8];
        var group = new HubGroup { Name = "PG lanes test", Slug = $"pg-lanes-{tag}", OwnerUserId = ownerId.Value };
        var canon = new Swimmer { LastName = "PgLanes", FirstName = $"Canon {tag}", BirthYear = 1980, Origin = "local" };
        var dup = new Swimmer { LastName = "PgLanes", FirstName = $"Dup {tag}", BirthYear = 1980, Origin = "local" };
        probe.AddRange(group, canon, dup);
        await probe.SaveChangesAsync();

        try
        {
            probe.HubGroupMembers.AddRange(
                new HubGroupMember { HubGroupId = group.Id, SwimmerId = canon.Id, SortOrder = 1 },
                new HubGroupMember { HubGroupId = group.Id, SwimmerId = dup.Id, SortOrder = 2 });
            await probe.SaveChangesAsync();

            await using (var db = NewContext())
            {
                var levels = new HubGroupLevelService(db);
                var seeded = (await levels.GetAsync(group.Id))!;
                Assert.Equal(4, seeded.Levels.Count);
                var fast = seeded.Levels[0].Id;
                var slow = seeded.Levels[1].Id;
                Assert.True((await levels.SetSwimmerLevelAsync(group.Id, canon.Id, fast)).Success);
                Assert.True((await levels.SetSwimmerLevelAsync(group.Id, dup.Id, slow)).Success);

                var plans = new LanePlanService(db);
                var day = new DateOnly(2026, 9, 27);
                var lanes = new List<LanePlanLaneInputDto>
                {
                    new() { LaneNo = 1, LevelId = fast, Workout = "10x100" },
                    new() { LaneNo = 2, LevelId = slow },
                    new() { LaneNo = 3, LevelId = slow },
                };
                var (dist, error) = await plans.DistributeAsync(group.Id, new LanePlanDistributeInputDto { LaneCount = 3, Lanes = lanes });
                Assert.Null(error);
                var save = await plans.SaveAsync(group.Id, day,
                    new LanePlanInputDto { LaneCount = 3, Lanes = lanes, Swimmers = dist!.Swimmers }, ownerId.Value);
                Assert.True(save.Success, save.Error);

                // Сокращение 3 → 1: удаление строк дорожек и перенос людей одним SaveChanges.
                save = await plans.SaveAsync(group.Id, day, new LanePlanInputDto
                {
                    LaneCount = 1,
                    Lanes = [new() { LaneNo = 1, LevelId = slow }],
                    Swimmers = [new() { SwimmerId = dup.Id, LaneNo = 1 }, new() { SwimmerId = canon.Id, LaneNo = 1 }],
                }, ownerId.Value);
                Assert.True(save.Success, save.Error);
                Assert.True(await plans.SetStatusAsync(group.Id, day, LanePlanStatus.Published));

                var board = (await plans.GetAsync(group.Id, day, isManager: false))!;
                Assert.Equal([dup.Id, canon.Id], Assert.Single(board.Lanes).Swimmers.Select(s => s.SwimmerId));
            }

            await using (var db = NewContext())
            {
                var report = await new SwimmerMergeService(db)
                    .MergeAsync([new SwimmerMergePair(canon.Id, dup.Id)], dryRun: false);
                Assert.Equal("merged", report.Pairs.Single().Status);
            }

            await using (var check = NewContext())
            {
                // Уровень канонического главнее; место в плане — его строка, дубля нет.
                var levelRows = await check.HubGroupSwimmerLevels.AsNoTracking()
                    .Where(l => l.HubGroupId == group.Id).ToListAsync();
                Assert.Equal(canon.Id, Assert.Single(levelRows).SwimmerId);
                var places = await check.LanePlanSwimmers.AsNoTracking()
                    .Where(p => p.Plan!.HubGroupId == group.Id).ToListAsync();
                Assert.Equal(canon.Id, Assert.Single(places).SwimmerId);
                Assert.False(await check.Swimmers.AnyAsync(s => s.Id == dup.Id));
            }
        }
        finally
        {
            await using var cleanup = NewContext();
            await cleanup.HubGroups.Where(g => g.Id == group.Id).ExecuteDeleteAsync();
            await cleanup.Swimmers.Where(s => s.Id == canon.Id || s.Id == dup.Id).ExecuteDeleteAsync();
        }
    }
}
