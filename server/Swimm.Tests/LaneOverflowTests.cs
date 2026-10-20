using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Подуровни и перелив (решение Влада 20.10.2026, docs/plans/lane-plans-plan.md «Подуровни»):
/// лимит людей на дорожку в плане; при нехватке мест нижний подуровень уровня по одному человеку
/// уходит на дорожки следующего уровня — только вниз, только на соседний блок, только если там
/// есть место (с учётом цепочки). Плюс правила, сервис уровней и сервис плана.
/// </summary>
public class LaneOverflowTests
{
    // ── Чистые функции ──────────────────────────────────────────────────────

    /// <summary>Пловцы подряд с id 1..N, SortKey = id: (сколько, уровень, подуровень).</summary>
    private static List<LaneDistribution.Swimmer> People(params (int Count, int Level, int? Sub)[] groups)
    {
        var result = new List<LaneDistribution.Swimmer>();
        var id = 1;
        foreach (var (count, level, sub) in groups)
            for (var i = 0; i < count; i++, id++) result.Add(new LaneDistribution.Swimmer(id, level, id, sub));
        return result;
    }

    private static LaneDistribution.Lane[] Lanes(params int[] levelOfLane) =>
        levelOfLane.Select((level, i) => new LaneDistribution.Lane(i + 1, level)).ToArray();

    /// <summary>Пловцы дорожки в порядке дорожки (кто ведёт — первым).</summary>
    private static int[] On(IEnumerable<LaneDistribution.Placement> result, int laneNo) =>
        result.Where(p => p.LaneNo == laneNo).Select(p => p.SwimmerId).ToArray();

    // Advanced — 2 подуровня, Intermediate и Developing — по 2, Beginner — без подуровней.
    private static readonly LaneAllocation.Level[] Levels =
        [new(1, 1, 2), new(2, 2, 2), new(3, 3, 2), new(4, 4, 1)];

    [Fact]
    public void VladsExample_Advanced2GoesDownWhenTheLaneIsFull()
    {
        // 2 дорожки, лимит 6: Advanced 9 (Advanced 1 — шестеро, Advanced 2 — трое), Intermediate 3.
        var people = People((6, 1, 1), (3, 1, 2), (3, 2, 1));

        var result = LaneDistribution.Distribute(Lanes(1, 2), people, Levels, maxPerLane: 6);

        Assert.Equal([1, 2, 3, 4, 5, 6], On(result, 1));
        // Пришедшие сверху сильнее — ведут на дорожке Intermediate.
        Assert.Equal([7, 8, 9, 10, 11, 12], On(result, 2));
    }

    [Fact]
    public void NoLimit_NobodyMoves_EvenWithSubLevels()
    {
        var people = People((6, 1, 1), (3, 1, 2), (3, 2, 1));

        var result = LaneDistribution.Distribute(Lanes(1, 2), people, Levels);

        Assert.Equal(9, On(result, 1).Length);
        Assert.Equal([10, 11, 12], On(result, 2));
    }

    [Fact]
    public void OneByOne_OnlyAsManyAsNeeded_LastInOrderFirst()
    {
        // Лимит 7: лишних двое — уходят двое последних из Advanced 2, третий остаётся.
        var people = People((6, 1, 1), (3, 1, 2), (3, 2, 1));

        var result = LaneDistribution.Distribute(Lanes(1, 2), people, Levels, maxPerLane: 7);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7], On(result, 1));
        Assert.Equal([8, 9, 10, 11, 12], On(result, 2));
    }

    [Fact]
    public void OnlyDown_CrowdedWeakLevelDoesNotGoUp()
    {
        // Тесно у Intermediate (9 при лимите 6), у Advanced место есть — вверх никто не поднимается.
        var people = People((3, 1, null), (3, 2, 1), (6, 2, 2));

        var result = LaneDistribution.Distribute(Lanes(1, 2), people, Levels, maxPerLane: 6);

        Assert.Equal([1, 2, 3], On(result, 1));
        Assert.Equal(9, On(result, 2).Length);
    }

    [Fact]
    public void NoRoomBelow_StaysOverfull()
    {
        // Advanced 8 при лимите 6, но Intermediate уже полон и отдать вниз ему некого.
        var people = People((6, 1, 1), (2, 1, 2), (6, 4, null));

        var result = LaneDistribution.Distribute(Lanes(1, 4), people, Levels, maxPerLane: 6);

        Assert.Equal(8, On(result, 1).Length);
        Assert.Equal(6, On(result, 2).Length);
    }

    [Fact]
    public void Chain_LowerLevelMakesRoomByPassingItsOwnBottomDown()
    {
        // Лимит 6, 3 дорожки. Advanced 7 (лишний один); Intermediate полон (6), но его нижний
        // подуровень может уйти к Developing (2 человека) — цепочка освобождает место.
        var people = People((5, 1, 1), (2, 1, 2), (4, 2, 1), (2, 2, 2), (2, 3, null));

        var result = LaneDistribution.Distribute(Lanes(1, 2, 3), people, Levels, maxPerLane: 6);

        Assert.Equal([1, 2, 3, 4, 5, 6], On(result, 1));
        Assert.Equal([7, 8, 9, 10, 11, 12], On(result, 2));
        Assert.Equal([13, 14, 15], On(result, 3));
    }

    [Fact]
    public void MovedSwimmerGoesOnlyOneLevelDown()
    {
        // Лимит 5: Advanced 8, место у Intermediate — на одного; у Developing места много, но
        // Advanced 2 через уровень не прыгает, а своих нижних у Intermediate нет.
        var people = People((4, 1, 1), (4, 1, 2), (4, 2, null), (1, 3, null));

        var result = LaneDistribution.Distribute(Lanes(1, 2, 3), people, Levels, maxPerLane: 5);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7], On(result, 1));
        Assert.Equal([8, 9, 10, 11, 12], On(result, 2));
        Assert.Equal([13], On(result, 3));
    }

    [Fact]
    public void WithoutSubLevel_OrLevelWithoutSubLevels_DoesNotMove()
    {
        // У Advanced 2 подуровня, но никому подуровень не задан — уходить некому.
        var noSub = People((8, 1, null), (2, 2, 1));
        var result = LaneDistribution.Distribute(Lanes(1, 2), noSub, Levels, maxPerLane: 6);
        Assert.Equal(8, On(result, 1).Length);

        // Уровень без подуровней (SubLevels = 1): подуровень пловца не в счёт.
        LaneAllocation.Level[] flat = [new(1, 1, 1), new(2, 2, 1)];
        var flatPeople = People((8, 1, 1), (2, 2, null));
        var flatResult = LaneDistribution.Distribute(Lanes(1, 2), flatPeople, flat, maxPerLane: 6);
        Assert.Equal(8, On(flatResult, 1).Length);
    }

    [Fact]
    public void Distribute_FollowsRankNotLaneNumber_AndSubLevelOneLeads()
    {
        // Advanced на дорожке 2, Intermediate — на дорожке 1. Порядок состава: подуровни вперемешку.
        var people = new List<LaneDistribution.Swimmer>
        {
            new(1, 1, 1, 2), new(2, 1, 2, 1), new(3, 1, 3, 2), new(4, 1, 4, 1), new(5, 2, 5, null),
        };

        // Без лимита: внутри уровня подуровень 1 первым.
        var plain = LaneDistribution.Distribute(Lanes(2, 1), people, Levels);
        Assert.Equal([2, 4, 1, 3], On(plain, 2));

        // Лимит 3: перелив идёт к следующему по силе уровню (дорожка 1), а не к следующему номеру.
        var limited = LaneDistribution.Distribute(Lanes(2, 1), people, Levels, maxPerLane: 3);
        Assert.Equal([2, 4, 1], On(limited, 2));
        Assert.Equal([3, 5], On(limited, 1));
    }

    [Fact]
    public void Distribute_OverflowsIntoALaneWhoseLevelHasNobodyToday()
    {
        // У Intermediate сегодня никого, но дорожка у него есть — Advanced 2 переходит туда.
        var people = People((1, 1, 1), (1, 1, 2));

        var result = LaneDistribution.Distribute(Lanes(1, 2), people, Levels, maxPerLane: 1);

        Assert.Equal([1], On(result, 1));
        Assert.Equal([2], On(result, 2));
    }

    [Fact]
    public void AutoLanes_OverflowsTheBottomSubLevelIntoTheNextGroup()
    {
        // Тот же пример через Auto lanes: каждому уровню по дорожке, затем перелив.
        var people = People((6, 1, 1), (3, 1, 2), (3, 2, 1));

        var result = LaneAllocation.AutoLanes(2, Levels, people, maxPerLane: 6);

        Assert.Equal([(1, (int?)1), (2, 2)], result.Lanes.Select(l => (l.LaneNo, l.LevelId)));
        Assert.Equal([1, 2, 3, 4, 5, 6], On(result.Placements, 1));
        Assert.Equal([7, 8, 9, 10, 11, 12], On(result.Placements, 2));

        // Без лимита — как раньше.
        var plain = LaneAllocation.AutoLanes(2, Levels, people);
        Assert.Equal(9, On(plain.Placements, 1).Length);
    }

    [Fact]
    public void Placements_GoLaneByLane_UnassignedLast()
    {
        var people = People((2, 1, 2), (2, 1, 1)).Append(new LaneDistribution.Swimmer(9, null, 0)).ToList();

        var result = LaneDistribution.Distribute(Lanes(1), people, Levels);

        // Подуровень 1 ведёт, без уровня — в конце.
        Assert.Equal([(3, (int?)1), (4, 1), (1, 1), (2, 1), (9, null)], result.Select(p => (p.SwimmerId, p.LaneNo)));
    }

    // ── Правила ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void LevelRules_SubLevelsFrom1To5(int subLevels, bool ok)
    {
        var (_, error) = HubGroupLevelRules.Normalize([new HubGroupLevelInputDto { Name = "Advanced", SubLevels = subLevels }]);
        Assert.Equal(ok, error == null);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(20, true)]
    [InlineData(0, false)]
    [InlineData(21, false)]
    public void PlanRules_MaxPerLaneFrom1To20OrNone(int? maxPerLane, bool ok)
    {
        var (plan, error) = LanePlanRules.Normalize(new LanePlanInputDto { LaneCount = 2, MaxPerLane = maxPerLane });
        Assert.Equal(ok, error == null);
        if (ok) Assert.Equal(maxPerLane, plan!.MaxPerLane);
    }

    // ── Сервисы ─────────────────────────────────────────────────────────────

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private sealed record Seed(int GroupId, int[] Swimmers, int Advanced, int Intermediate);

    /// <summary>Группа из четырёх пловцов (по SortOrder); Advanced — 2 подуровня, Intermediate — без.</summary>
    private static async Task<Seed> SeedAsync(SwimmDbContext db)
    {
        var owner = new AppUser { Email = "coach@example.com", DisplayName = "Coach", SecurityStamp = "s" };
        db.AppUsers.Add(owner);
        await db.SaveChangesAsync();

        var group = new HubGroup { Name = "G", Slug = "g", OwnerUserId = owner.Id, IsPublic = true };
        var swimmers = Enumerable.Range(1, 4)
            .Select(i => new Swimmer { LastName = $"ש{i}", FirstName = "א", BirthYear = 1980 + i })
            .ToList();
        db.Add(group);
        db.AddRange(swimmers);
        await db.SaveChangesAsync();

        db.HubGroupMembers.AddRange(swimmers.Select((s, i) =>
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = s.Id, SortOrder = i + 1 }));
        var advanced = new HubGroupLevel { HubGroupId = group.Id, Rank = 1, Name = "Advanced", SubLevels = 2 };
        var intermediate = new HubGroupLevel { HubGroupId = group.Id, Rank = 2, Name = "Intermediate" };
        db.HubGroupLevels.AddRange(advanced, intermediate);
        await db.SaveChangesAsync();

        return new Seed(group.Id, swimmers.Select(s => s.Id).ToArray(), advanced.Id, intermediate.Id);
    }

    [Fact]
    public async Task SetSwimmerLevel_StoresSubLevel_IgnoresItOnFlatLevel_RejectsOutOfRange()
    {
        await using var db = CreateDb(nameof(SetSwimmerLevel_StoresSubLevel_IgnoresItOnFlatLevel_RejectsOutOfRange));
        var s = await SeedAsync(db);
        var service = new HubGroupLevelService(db);
        var (a, b) = (s.Swimmers[0], s.Swimmers[1]);

        Assert.True((await service.SetSwimmerLevelAsync(s.GroupId, a, s.Advanced, 2)).Success);
        Assert.True((await service.SetSwimmerLevelAsync(s.GroupId, b, s.Intermediate, 2)).Success);
        Assert.False((await service.SetSwimmerLevelAsync(s.GroupId, b, s.Advanced, 3)).Success);

        var dto = (await service.GetAsync(s.GroupId))!;
        Assert.Equal(2, dto.Levels.Single(l => l.Id == s.Advanced).SubLevels);
        Assert.Equal((s.Advanced, 2), dto.Swimmers.Where(w => w.SwimmerId == a).Select(w => (w.LevelId!.Value, w.SubLevel!.Value)).Single());
        // У Intermediate подуровней нет — подуровень не хранится; неудачный вызов ничего не поменял.
        Assert.Equal((s.Intermediate, (int?)null), dto.Swimmers.Where(w => w.SwimmerId == b).Select(w => (w.LevelId!.Value, w.SubLevel)).Single());

        // Смена уровня без подуровня снимает и подуровень.
        Assert.True((await service.SetSwimmerLevelAsync(s.GroupId, a, s.Advanced)).Success);
        Assert.Null((await db.HubGroupSwimmerLevels.SingleAsync(l => l.SwimmerId == a)).SubLevel);
    }

    [Fact]
    public async Task SaveLevels_FewerSubLevels_ClampsSwimmers_NullKeepsCount()
    {
        await using var db = CreateDb(nameof(SaveLevels_FewerSubLevels_ClampsSwimmers_NullKeepsCount));
        var s = await SeedAsync(db);
        var service = new HubGroupLevelService(db);
        HubGroupLevelInputDto Advanced(int? subLevels) => new() { Id = s.Advanced, Name = "Advanced", SubLevels = subLevels };
        HubGroupLevelInputDto Intermediate() => new() { Id = s.Intermediate, Name = "Intermediate" };

        Assert.True((await service.SaveLevelsAsync(s.GroupId, new() { Levels = [Advanced(3), Intermediate()] })).Success);
        await service.SetSwimmerLevelAsync(s.GroupId, s.Swimmers[0], s.Advanced, 3);
        await service.SetSwimmerLevelAsync(s.GroupId, s.Swimmers[1], s.Advanced, 1);

        // SubLevels не прислан — остаётся 3.
        Assert.True((await service.SaveLevelsAsync(s.GroupId, new() { Levels = [Advanced(null), Intermediate()] })).Success);
        Assert.Equal(3, (await db.HubGroupLevels.SingleAsync(l => l.Id == s.Advanced)).SubLevels);

        // 3 → 2: «Advanced 3» становится «Advanced 2», «Advanced 1» на месте.
        Assert.True((await service.SaveLevelsAsync(s.GroupId, new() { Levels = [Advanced(2), Intermediate()] })).Success);
        Assert.Equal(2, (await db.HubGroupSwimmerLevels.SingleAsync(l => l.SwimmerId == s.Swimmers[0])).SubLevel);
        Assert.Equal(1, (await db.HubGroupSwimmerLevels.SingleAsync(l => l.SwimmerId == s.Swimmers[1])).SubLevel);

        // 2 → 1: подуровней нет — уровень остаётся, подуровень снят.
        Assert.True((await service.SaveLevelsAsync(s.GroupId, new() { Levels = [Advanced(1), Intermediate()] })).Success);
        Assert.All(await db.HubGroupSwimmerLevels.ToListAsync(), l => { Assert.Equal(s.Advanced, l.LevelId); Assert.Null(l.SubLevel); });
    }

    [Fact]
    public async Task LanePlan_MaxPerLane_IsSaved_AndDrivesDistributeAndAutoLanes()
    {
        await using var db = CreateDb(nameof(LanePlan_MaxPerLane_IsSaved_AndDrivesDistributeAndAutoLanes));
        var s = await SeedAsync(db);
        var levels = new HubGroupLevelService(db);
        var service = new LanePlanService(db);
        var (p1, p2, p3, p4) = (s.Swimmers[0], s.Swimmers[1], s.Swimmers[2], s.Swimmers[3]);
        // Advanced: p1 — 1, p2 и p3 — 2; Intermediate: p4.
        await levels.SetSwimmerLevelAsync(s.GroupId, p1, s.Advanced, 1);
        await levels.SetSwimmerLevelAsync(s.GroupId, p2, s.Advanced, 2);
        await levels.SetSwimmerLevelAsync(s.GroupId, p3, s.Advanced, 2);
        await levels.SetSwimmerLevelAsync(s.GroupId, p4, s.Intermediate);
        List<LanePlanLaneInputDto> lanes = [new() { LaneNo = 1, LevelId = s.Advanced }, new() { LaneNo = 2, LevelId = s.Intermediate }];

        var (distributed, error) = await service.DistributeAsync(s.GroupId,
            new LanePlanDistributeInputDto { LaneCount = 2, MaxPerLane = 2, Lanes = lanes });
        Assert.Null(error);
        Assert.Equal([(p1, (int?)1), (p2, 1), (p3, 2), (p4, 2)], distributed!.Swimmers.Select(x => (x.SwimmerId, x.LaneNo)));

        var (auto, autoError) = await service.AutoLanesAsync(s.GroupId,
            new LanePlanAutoLanesInputDto { LaneCount = 2, MaxPerLane = 2 });
        Assert.Null(autoError);
        Assert.Equal([(p1, (int?)1), (p2, 1), (p3, 2), (p4, 2)], auto!.Swimmers.Select(x => (x.SwimmerId, x.LaneNo)));

        var (_, bad) = await service.DistributeAsync(s.GroupId,
            new LanePlanDistributeInputDto { LaneCount = 2, MaxPerLane = 0, Lanes = lanes });
        Assert.NotNull(bad);

        // Лимит сохраняется с планом и приходит в доске и в списке; у пловца — подуровень.
        var day = new DateOnly(2026, 10, 20);
        var saved = await service.SaveAsync(s.GroupId, day, new LanePlanInputDto
        {
            LaneCount = 2, MaxPerLane = 2, Lanes = lanes, Swimmers = distributed.Swimmers,
        }, userId: 1);
        Assert.True(saved.Success, saved.Error);
        var board = (await service.GetAsync(s.GroupId, day, isManager: true))!;
        Assert.Equal(2, board.MaxPerLane);
        Assert.Equal([p3, p4], board.Lanes[1].Swimmers.Select(x => x.SwimmerId));
        Assert.Equal(2, board.Lanes[1].Swimmers[0].SubLevel);
        Assert.Equal(2, (await service.ListAsync(s.GroupId, isManager: true)).Single().MaxPerLane);
    }
}
