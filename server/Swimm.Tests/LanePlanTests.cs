using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// План дорожек (docs/plans/lane-plans-plan.md, L2): раскладка «Distribute»
/// (<see cref="LaneDistribution"/>), форма плана (<see cref="LanePlanRules"/>) и сервис
/// (<see cref="LanePlanService"/>) — черновик/публикация, снимок, корзины Unassigned / Not today.
/// </summary>
public class LanePlanTests
{
    // ── LaneDistribution ────────────────────────────────────────────────────

    private static List<LaneDistribution.Swimmer> Swimmers(params (int Count, int? Level)[] groups)
    {
        var result = new List<LaneDistribution.Swimmer>();
        var id = 1;
        foreach (var (count, level) in groups)
            for (var i = 0; i < count; i++, id++) result.Add(new LaneDistribution.Swimmer(id, level, id));
        return result;
    }

    [Fact]
    public void Distribute_DolphinExample_SplitsEachLevelEvenly()
    {
        // Пример из плана: 20 пловцов, дорожка 1 — уровень 1, 2–3 — 2, 4–5 — 3, 6 — 4.
        var lanes = new[]
        {
            new LaneDistribution.Lane(1, 1), new LaneDistribution.Lane(2, 2), new LaneDistribution.Lane(3, 2),
            new LaneDistribution.Lane(4, 3), new LaneDistribution.Lane(5, 3), new LaneDistribution.Lane(6, 4),
        };
        var swimmers = Swimmers((4, 1), (7, 2), (6, 3), (3, 4));

        var result = LaneDistribution.Distribute(lanes, swimmers);

        var perLane = result.GroupBy(p => p.LaneNo).ToDictionary(g => g.Key!.Value, g => g.Count());
        Assert.Equal(new Dictionary<int, int> { [1] = 4, [2] = 4, [3] = 3, [4] = 3, [5] = 3, [6] = 3 }, perLane);
        Assert.All(result, p => Assert.NotNull(p.LaneNo));
    }

    [Fact]
    public void Distribute_ChunksInOrder_NotRoundRobin()
    {
        var lanes = new[] { new LaneDistribution.Lane(3, 7), new LaneDistribution.Lane(2, 7) };
        var swimmers = Swimmers((5, 7));

        var result = LaneDistribution.Distribute(lanes, swimmers);

        // Первые по порядку — на меньший номер дорожки, подряд; лишний — в первую дорожку.
        Assert.Equal([2, 2, 2, 3, 3], result.Select(p => p.LaneNo!.Value));
        Assert.Equal([1, 2, 3, 4, 5], result.Select(p => p.SwimmerId));
    }

    [Fact]
    public void Distribute_NoLevelOrLevelWithoutLanes_IsUnassigned()
    {
        var lanes = new[] { new LaneDistribution.Lane(1, 1), new LaneDistribution.Lane(2, null) };
        var swimmers = Swimmers((1, 1), (1, null), (1, 9));

        var result = LaneDistribution.Distribute(lanes, swimmers);

        Assert.Equal([1, null, null], result.Select(p => p.LaneNo));
    }

    [Fact]
    public void Distribute_IsDeterministic_AndFollowsSortKey()
    {
        var lanes = new[] { new LaneDistribution.Lane(1, 1), new LaneDistribution.Lane(2, 1) };
        var swimmers = new[]
        {
            new LaneDistribution.Swimmer(10, 1, 3), new LaneDistribution.Swimmer(11, 1, 1),
            new LaneDistribution.Swimmer(12, 1, 2), new LaneDistribution.Swimmer(13, 1, 0),
        };

        var first = LaneDistribution.Distribute(lanes, swimmers);
        var second = LaneDistribution.Distribute(lanes, swimmers.Reverse());

        Assert.Equal(first, second);
        Assert.Equal([13, 11, 12, 10], first.Select(p => p.SwimmerId));
        Assert.Equal([1, 1, 2, 2], first.Select(p => p.LaneNo!.Value));
    }

    // ── LanePlanRules ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-09-27", true)]
    [InlineData("2026-9-27", false)]
    [InlineData("27.09.2026", false)]
    [InlineData("1999-12-31", false)]
    [InlineData("", false)]
    public void TryParseDate_StrictFormat(string raw, bool ok) =>
        Assert.Equal(ok, LanePlanRules.TryParseDate(raw, out _));

    [Fact]
    public void Normalize_FillsMissingLanes_AndOrdersWithinLane()
    {
        var (plan, error) = LanePlanRules.Normalize(new LanePlanInputDto
        {
            LaneCount = 3,
            Note = "  ",
            Lanes = [new() { LaneNo = 2, LevelId = 5, Workout = " 4x100 " }],
            Swimmers =
            [
                new() { SwimmerId = 1, LaneNo = 2 }, new() { SwimmerId = 2, LaneNo = null },
                new() { SwimmerId = 3, LaneNo = 2 }, new() { SwimmerId = 4, LaneNo = null },
            ],
        });

        Assert.Null(error);
        Assert.Null(plan!.Note);
        Assert.Equal(
            [new LanePlanRules.NormalizedLane(1, null, null), new(2, 5, "4x100"), new(3, null, null)],
            plan.Lanes);
        Assert.Equal(
            [new LanePlanRules.NormalizedSwimmer(1, 2, 0), new(2, null, 0), new(3, 2, 1), new(4, null, 1)],
            plan.Swimmers);
    }

    public static TheoryData<LanePlanInputDto> InvalidPlans => new()
    {
        new LanePlanInputDto { LaneCount = 0 },
        new LanePlanInputDto { LaneCount = LanePlanRules.MaxLanes + 1 },
        new LanePlanInputDto { LaneCount = 2, Lanes = [new() { LaneNo = 3 }] },
        new LanePlanInputDto { LaneCount = 2, Lanes = [new() { LaneNo = 1 }, new() { LaneNo = 1 }] },
        new LanePlanInputDto { LaneCount = 2, Swimmers = [new() { SwimmerId = 1, LaneNo = 3 }] },
        new LanePlanInputDto { LaneCount = 2, Swimmers = [new() { SwimmerId = 1 }, new() { SwimmerId = 1, LaneNo = 2 }] },
        new LanePlanInputDto { LaneCount = 1, Note = new string('x', LanePlanRules.NoteMaxLength + 1) },
        new LanePlanInputDto { LaneCount = 1, Lanes = [new() { LaneNo = 1, Workout = new string('x', LanePlanRules.WorkoutMaxLength + 1) }] },
    };

    [Theory]
    [MemberData(nameof(InvalidPlans))]
    public void Normalize_RejectsInvalidPlan(LanePlanInputDto input)
    {
        var (plan, error) = LanePlanRules.Normalize(input);

        Assert.Null(plan);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    // ── LanePlanService ─────────────────────────────────────────────────────

    private static readonly DateOnly Day = new(2026, 9, 27);

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    /// <summary>Группа: A, B, C в составе (по SortOrder), Hidden — скрытый клубный; Outsider вне группы.</summary>
    private sealed record Seed(int GroupId, int OtherGroupId, int UserId, int A, int B, int C, int Hidden, int Outsider,
        int Fast, int Slow, int ForeignLevel);

    private static async Task<Seed> SeedAsync(SwimmDbContext db)
    {
        var owner = new AppUser { Email = "coach@example.com", DisplayName = "Coach", SecurityStamp = "s" };
        db.AppUsers.Add(owner);
        await db.SaveChangesAsync();

        var group = new HubGroup { Name = "G", Slug = "g", OwnerUserId = owner.Id, IsPublic = true };
        var other = new HubGroup { Name = "O", Slug = "o", OwnerUserId = owner.Id, IsPublic = true };
        var a = new Swimmer { LastName = "א", FirstName = "א", BirthYear = 1980 };
        var b = new Swimmer { LastName = "ב", FirstName = "ב", BirthYear = 1981 };
        var c = new Swimmer { LastName = "ג", FirstName = "ג", LastNameEn = "Gimel", BirthYear = 1982 };
        var hidden = new Swimmer { LastName = "ד", FirstName = "ד", BirthYear = 1983 };
        var outsider = new Swimmer { LastName = "ה", FirstName = "ה", BirthYear = 1984 };
        db.AddRange(group, other, a, b, c, hidden, outsider);
        await db.SaveChangesAsync();

        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = a.Id, SortOrder = 1 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = b.Id, SortOrder = 2 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = c.Id, SortOrder = 3 },
            new HubGroupMember
            {
                HubGroupId = group.Id, SwimmerId = hidden.Id, SortOrder = 4,
                Source = HubGroupMemberSource.Club, IsExcluded = true,
            });

        var fast = new HubGroupLevel { HubGroupId = group.Id, Rank = 1, Name = "Fast", Color = "#e53935" };
        var slow = new HubGroupLevel { HubGroupId = group.Id, Rank = 2, Name = "Slow" };
        var foreign = new HubGroupLevel { HubGroupId = other.Id, Rank = 1, Name = "Theirs" };
        db.HubGroupLevels.AddRange(fast, slow, foreign);
        await db.SaveChangesAsync();

        db.HubGroupSwimmerLevels.AddRange(
            new HubGroupSwimmerLevel { HubGroupId = group.Id, SwimmerId = a.Id, LevelId = fast.Id },
            new HubGroupSwimmerLevel { HubGroupId = group.Id, SwimmerId = b.Id, LevelId = slow.Id });
        await db.SaveChangesAsync();

        return new Seed(group.Id, other.Id, owner.Id, a.Id, b.Id, c.Id, hidden.Id, outsider.Id, fast.Id, slow.Id, foreign.Id);
    }

    private static LanePlanInputDto Plan(Seed s) => new()
    {
        LaneCount = 3,
        Note = "Main set",
        Lanes =
        [
            new() { LaneNo = 1, LevelId = s.Fast, Workout = "10x100 @1:30" },
            new() { LaneNo = 2, LevelId = s.Slow, Workout = "6x100 @2:00" },
        ],
        Swimmers =
        [
            new() { SwimmerId = s.A, LaneNo = 1 },
            new() { SwimmerId = s.B, LaneNo = 2 },
            new() { SwimmerId = s.C, LaneNo = null },
        ],
    };

    [Fact]
    public async Task Save_CreatesDraft_AndBoardHasAllBuckets()
    {
        await using var db = CreateDb(nameof(Save_CreatesDraft_AndBoardHasAllBuckets));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);
        var input = Plan(s);
        input.Swimmers.RemoveAt(2);  // C сегодня не плывёт

        var result = await service.SaveAsync(s.GroupId, Day, input, s.UserId);

        Assert.True(result.Success, result.Error);
        var board = (await service.GetAsync(s.GroupId, Day, isManager: true))!;
        Assert.Equal(LanePlanStatus.Draft, board.Status);
        Assert.Equal("2026-09-27", board.Date);
        Assert.Equal([1, 2, 3], board.Lanes.Select(l => l.LaneNo));
        Assert.Equal("Fast", board.Lanes[0].Level!.Name);
        Assert.Equal("#e53935", board.Lanes[0].Level!.Color);
        Assert.Null(board.Lanes[2].Level);
        Assert.Equal([s.A], board.Lanes[0].Swimmers.Select(w => w.SwimmerId));
        Assert.Equal(s.Fast, board.Lanes[0].Swimmers[0].LevelId);
        Assert.Empty(board.Unassigned);
        // Скрытый клубный — не состав, в Not today его нет.
        Assert.Equal([s.C], board.NotToday.Select(w => w.SwimmerId));
        Assert.Equal("ג ג", board.NotToday[0].Name);
        Assert.Equal("Gimel", board.NotToday[0].NameEn);
        Assert.True(board.CanEdit);
    }

    [Fact]
    public async Task Draft_IsHiddenFromMembers_PublishedShowsWithoutNotToday()
    {
        await using var db = CreateDb(nameof(Draft_IsHiddenFromMembers_PublishedShowsWithoutNotToday));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);
        var input = Plan(s);
        input.Swimmers.RemoveAt(2);
        await service.SaveAsync(s.GroupId, Day, input, s.UserId);

        Assert.Null(await service.GetAsync(s.GroupId, Day, isManager: false));
        Assert.Empty(await service.ListAsync(s.GroupId, isManager: false));
        Assert.Single(await service.ListAsync(s.GroupId, isManager: true));

        Assert.True(await service.SetStatusAsync(s.GroupId, Day, LanePlanStatus.Published));
        var board = (await service.GetAsync(s.GroupId, Day, isManager: false))!;
        Assert.Equal(LanePlanStatus.Published, board.Status);
        Assert.Empty(board.NotToday);
        Assert.False(board.CanEdit);
        Assert.Equal("published", Assert.Single(await service.ListAsync(s.GroupId, isManager: false)).Status);

        // Повторное сохранение статус не сбрасывает; снять публикацию — отдельная ручка.
        await service.SaveAsync(s.GroupId, Day, input, s.UserId);
        Assert.NotNull(await service.GetAsync(s.GroupId, Day, isManager: false));
        Assert.True(await service.SetStatusAsync(s.GroupId, Day, LanePlanStatus.Draft));
        Assert.Null(await service.GetAsync(s.GroupId, Day, isManager: false));
    }

    [Fact]
    public async Task Save_ReplacesInPlace_ShrinksLanes_KeepsOrderWithinLane()
    {
        await using var db = CreateDb(nameof(Save_ReplacesInPlace_ShrinksLanes_KeepsOrderWithinLane));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);
        await service.SaveAsync(s.GroupId, Day, Plan(s), s.UserId);

        // Две дорожки вместо трёх; все на первой, C ведёт; B снят с плана.
        var result = await service.SaveAsync(s.GroupId, Day, new LanePlanInputDto
        {
            LaneCount = 2,
            Lanes = [new() { LaneNo = 1, LevelId = s.Slow, Workout = "Easy" }],
            Swimmers = [new() { SwimmerId = s.C, LaneNo = 1 }, new() { SwimmerId = s.A, LaneNo = 1 }],
        }, s.UserId);

        Assert.True(result.Success, result.Error);
        var board = (await service.GetAsync(s.GroupId, Day, isManager: true))!;
        Assert.Equal([1, 2], board.Lanes.Select(l => l.LaneNo));
        Assert.Equal("Slow", board.Lanes[0].Level!.Name);
        Assert.Null(board.Lanes[1].Workout);
        Assert.Equal([s.C, s.A], board.Lanes[0].Swimmers.Select(w => w.SwimmerId));
        Assert.Equal([s.B], board.NotToday.Select(w => w.SwimmerId));
        Assert.Null(board.Note);
        Assert.Equal(2, await db.LanePlanLanes.CountAsync());
        Assert.Equal(2, await db.LanePlanSwimmers.CountAsync());
    }

    [Fact]
    public async Task Save_RejectsOutsiderHiddenAndForeignLevel()
    {
        await using var db = CreateDb(nameof(Save_RejectsOutsiderHiddenAndForeignLevel));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);

        var outsider = Plan(s);
        outsider.Swimmers.Add(new() { SwimmerId = s.Outsider, LaneNo = 1 });
        var hidden = Plan(s);
        hidden.Swimmers.Add(new() { SwimmerId = s.Hidden, LaneNo = 1 });
        var foreign = Plan(s);
        foreign.Lanes[1].LevelId = s.ForeignLevel;

        Assert.False((await service.SaveAsync(s.GroupId, Day, outsider, s.UserId)).Success);
        Assert.False((await service.SaveAsync(s.GroupId, Day, hidden, s.UserId)).Success);
        Assert.False((await service.SaveAsync(s.GroupId, Day, foreign, s.UserId)).Success);
        Assert.False(await db.LanePlans.AnyAsync());
    }

    [Fact]
    public async Task Snapshot_KeepsSwimmerWhoLeftTheGroup()
    {
        await using var db = CreateDb(nameof(Snapshot_KeepsSwimmerWhoLeftTheGroup));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);
        await service.SaveAsync(s.GroupId, Day, Plan(s), s.UserId);

        db.HubGroupMembers.Remove(await db.HubGroupMembers.SingleAsync(m => m.SwimmerId == s.B));
        await db.SaveChangesAsync();

        var board = (await service.GetAsync(s.GroupId, Day, isManager: true))!;
        var gone = Assert.Single(board.Lanes[1].Swimmers);
        Assert.Equal(s.B, gone.SwimmerId);
        Assert.True(gone.LeftGroup);
        Assert.Equal("ב ב", gone.Name);

        // Пересохранить план с ушедшим можно — он уже стоял в этом плане.
        Assert.True((await service.SaveAsync(s.GroupId, Day, Plan(s), s.UserId)).Success);
        // А в новый план на другой день — нельзя.
        Assert.False((await service.SaveAsync(s.GroupId, Day.AddDays(1), Plan(s), s.UserId)).Success);
    }

    [Fact]
    public async Task Distribute_UsesCurrentLevelsAndRosterOrder()
    {
        await using var db = CreateDb(nameof(Distribute_UsesCurrentLevelsAndRosterOrder));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);

        var (result, error) = await service.DistributeAsync(s.GroupId, new LanePlanDistributeInputDto
        {
            LaneCount = 3,
            Lanes = [new() { LaneNo = 1, LevelId = s.Fast }, new() { LaneNo = 3, LevelId = s.Slow }],
        });

        Assert.Null(error);
        Assert.Equal(
            [(s.A, (int?)1), (s.B, 3), (s.C, null)],
            result!.Swimmers.Select(p => (p.SwimmerId, p.LaneNo)));
        Assert.False(await db.LanePlans.AnyAsync());  // ничего не сохранено

        // Пришли только B и C.
        var (partial, _) = await service.DistributeAsync(s.GroupId, new LanePlanDistributeInputDto
        {
            LaneCount = 3, Lanes = [new() { LaneNo = 3, LevelId = s.Slow }], SwimmerIds = [s.C, s.B],
        });
        Assert.Equal([(s.B, (int?)3), (s.C, null)], partial!.Swimmers.Select(p => (p.SwimmerId, p.LaneNo)));
    }

    [Fact]
    public async Task RemovingLevel_ClearsLaneLabel_PlanStays()
    {
        await using var db = CreateDb(nameof(RemovingLevel_ClearsLaneLabel_PlanStays));
        var s = await SeedAsync(db);
        await new LanePlanService(db).SaveAsync(s.GroupId, Day, Plan(s), s.UserId);

        var levels = new HubGroupLevelService(db);
        var saved = await levels.SaveLevelsAsync(s.GroupId, new HubGroupLevelsInputDto
        {
            Levels = [new() { Id = s.Slow, Name = "Slow" }],
        });

        Assert.True(saved.Success, saved.Error);
        var board = (await new LanePlanService(db).GetAsync(s.GroupId, Day, isManager: true))!;
        Assert.Null(board.Lanes[0].Level);
        Assert.Equal([s.A], board.Lanes[0].Swimmers.Select(w => w.SwimmerId));
        Assert.Equal("Slow", board.Lanes[1].Level!.Name);
    }

    [Fact]
    public async Task MySwimmers_AllSourcesInOrder_OnlyThoseInPlan()
    {
        await using var db = CreateDb(nameof(MySwimmers_AllSourcesInOrder_OnlyThoseInPlan));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);
        var input = Plan(s);
        input.Swimmers.RemoveAt(2);  // C — Not today
        await service.SaveAsync(s.GroupId, Day, input, s.UserId);
        await service.SetStatusAsync(s.GroupId, Day, LanePlanStatus.Published);

        var viewer = new AppUser { Email = "parent@example.com", DisplayName = "P", SecurityStamp = "s", SwimmerId = s.B };
        var stranger = new AppUser { Email = "x@example.com", DisplayName = "X", SecurityStamp = "s" };
        db.AppUsers.AddRange(viewer, stranger);
        await db.SaveChangesAsync();
        db.UserFavorites.AddRange(
            new UserFavorite { UserId = viewer.Id, TargetType = "swimmer", SwimmerId = s.A, IsFamily = true },
            new UserFavorite { UserId = viewer.Id, TargetType = "swimmer", SwimmerId = s.C, IsFamily = true },   // не в плане
            new UserFavorite { UserId = viewer.Id, TargetType = "swimmer", SwimmerId = s.B, IsPrimary = true },  // дубль привязки
            new UserFavorite { UserId = stranger.Id, TargetType = "swimmer", SwimmerId = s.A, IsPrimary = true });
        await db.SaveChangesAsync();

        var board = (await service.GetAsync(s.GroupId, Day, isManager: false, viewer.Id))!;
        Assert.Equal([(s.B, "me"), (s.A, "family")], board.MySwimmers.Select(m => (m.SwimmerId, m.Kind)));

        // Без зрителя — пусто; чужое избранное не протекает.
        Assert.Empty((await service.GetAsync(s.GroupId, Day, isManager: false))!.MySwimmers);
        var strangers = (await service.GetAsync(s.GroupId, Day, isManager: false, stranger.Id))!.MySwimmers;
        Assert.Equal([(s.A, "me")], strangers.Select(m => (m.SwimmerId, m.Kind)));
    }

    [Fact]
    public async Task MySwimmers_MembershipLabel_IsFamily_AndOnlyInThisGroup()
    {
        await using var db = CreateDb(nameof(MySwimmers_MembershipLabel_IsFamily_AndOnlyInThisGroup));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);
        await service.SaveAsync(s.GroupId, Day, Plan(s), s.UserId);
        await service.SetStatusAsync(s.GroupId, Day, LanePlanStatus.Published);

        var viewer = new AppUser { Email = "m@example.com", DisplayName = "M", SecurityStamp = "s" };
        db.AppUsers.Add(viewer);
        await db.SaveChangesAsync();
        db.HubGroupUserMembers.AddRange(
            new HubGroupUserMember { HubGroupId = s.OtherGroupId, UserId = viewer.Id, SwimmerId = s.A },
            new HubGroupUserMember { HubGroupId = s.GroupId, UserId = viewer.Id, SwimmerId = s.C });
        await db.SaveChangesAsync();

        var board = (await service.GetAsync(s.GroupId, Day, isManager: false, viewer.Id))!;

        // C стоит в Unassigned — он в плане, подсветка есть; метка из другой группы (A) — нет.
        Assert.Equal([(s.C, "family")], board.MySwimmers.Select(m => (m.SwimmerId, m.Kind)));
    }

    [Fact]
    public async Task Delete_RemovesPlanWithRows()
    {
        await using var db = CreateDb(nameof(Delete_RemovesPlanWithRows));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);
        await service.SaveAsync(s.GroupId, Day, Plan(s), s.UserId);

        Assert.True(await service.DeleteAsync(s.GroupId, Day));
        Assert.False(await service.DeleteAsync(s.GroupId, Day));
        Assert.False(await service.SetStatusAsync(s.GroupId, Day, LanePlanStatus.Published));
        Assert.False(await db.LanePlanLanes.AnyAsync());
        Assert.False(await db.LanePlanSwimmers.AnyAsync());
    }
}
