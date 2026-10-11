using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Вид по дорожкам (Ш3.2, docs/plans/entity-hero-roles-plan.md §5): чистая раскладка
/// <see cref="TrainingLaneView"/> и её сборка в ответе RSVP — кем стоит аккаунт (метка тренера →
/// привязка админом → «Me» → «Family» → сам аккаунт), уровни, «Who's coming».
/// </summary>
public class TrainingLaneViewTests
{
    private static readonly TrainingLaneView.LevelInfo Fast = new(1, 1, "Fast", "#e53935");
    private static readonly TrainingLaneView.LevelInfo Mid = new(2, 2, "Mid", null);
    private static readonly TrainingLaneView.LevelInfo Slow = new(3, 3, "Slow", null);
    private static readonly TrainingLaneView.LevelInfo[] Levels = [Fast, Mid, Slow];

    private static TrainingLaneView.Person P(int userId, int? level = null, int? swimmerId = null,
        string answer = "yes", int claims = 1) =>
        new(userId, swimmerId, $"U{userId}", "female", answer, null, level, userId, claims);

    private static TrainingLaneViewDto Auto(IReadOnlyList<TrainingLaneView.Person> pool, int? lanes,
        int viewer = 1, bool namesVisible = true, int? lastPlan = null) =>
        TrainingLaneView.Build(GroupLaneView.Auto, null, lanes, lastPlan, Levels, pool, viewer, namesVisible)!;

    private static int[] Sizes(TrainingLaneViewDto dto) => dto.Lanes.Select(l => l.People.Count).ToArray();

    // ── Режимы ──────────────────────────────────────────────────────────────

    [Fact]
    public void Off_NoView_PlanOnlyWithoutPlan_NoView()
    {
        Assert.Null(TrainingLaneView.Build(GroupLaneView.Off, null, 4, null, Levels, [P(1)], 1, true));
        Assert.Null(TrainingLaneView.Build(GroupLaneView.Plan, null, 4, null, Levels, [P(1)], 1, true));
    }

    [Fact]
    public void UnknownLaneCount_OneCommonWater_InRosterOrder()
    {
        var dto = Auto([P(3), P(1, level: 1), P(2)], lanes: null);

        Assert.Equal(TrainingLaneView.SourceWater, dto.Source);
        Assert.Equal([1, 2, 3], Assert.Single(dto.Lanes).People.Select(p => p.UserId!.Value));
        Assert.Empty(dto.NoLane);
    }

    [Fact]
    public void LastPlanLaneCount_UsedWhenNoUsualLanes()
    {
        var dto = Auto([P(1), P(2), P(3)], lanes: null, lastPlan: 3);

        Assert.Equal((TrainingLaneView.SourceAuto, 3), (dto.Source, dto.LaneCount));
    }

    // ── Авто-раскладка ──────────────────────────────────────────────────────

    [Fact]
    public void NoLevels_ThirteenOnThreeLanes_ConsecutiveChunks()
    {
        var pool = Enumerable.Range(1, 13).Select(i => P(i)).ToList();

        var dto = Auto(pool, lanes: 3);

        Assert.Equal([5, 4, 4], Sizes(dto));
        Assert.Equal([1, 2, 3, 4, 5], dto.Lanes[0].People.Select(p => p.UserId!.Value));
        Assert.All(dto.Lanes, l => Assert.Null(l.Level));
        Assert.Empty(dto.NoLane);
    }

    [Fact]
    public void Levels_OneLanePerLevel_WithoutLevelGoesToNoLane()
    {
        var pool = new List<TrainingLaneView.Person>();
        pool.AddRange(Enumerable.Range(1, 5).Select(i => P(i, level: 1)));
        pool.AddRange(Enumerable.Range(6, 5).Select(i => P(i, level: 2)));
        pool.AddRange(Enumerable.Range(11, 3).Select(i => P(i, level: 3)));
        pool.Add(P(20));

        var dto = Auto(pool, lanes: 3);

        Assert.Equal([5, 5, 3], Sizes(dto));
        Assert.Equal(["Fast", "Mid", "Slow"], dto.Lanes.Select(l => l.Level!.Name));
        Assert.Equal("#e53935", dto.Lanes[0].Level!.Color);
        Assert.Equal([20], dto.NoLane.Select(p => p.UserId!.Value));
    }

    [Fact]
    public void Levels_FewerLanes_NeighboursMerge_MaybeTakesAPlace()
    {
        var pool = new[] { P(1, level: 1), P(2, level: 2, answer: "maybe"), P(3, level: 3) };

        var dto = Auto(pool, lanes: 2);

        Assert.Equal(2, dto.Lanes.Count);
        Assert.Equal(3, dto.Lanes.Sum(l => l.People.Count));
        Assert.Contains(dto.Lanes.SelectMany(l => l.People), p => p.Answer == "maybe");
    }

    // ── План ────────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_PlacesBySwimmer_OrderFromPlan_OthersNoLane()
    {
        var plan = new TrainingLaneView.Plan(
            2,
            [new TrainingLaneView.PlanLane(1, 1, "10x100"), new TrainingLaneView.PlanLane(2, null, null)],
            new Dictionary<int, (int? LaneNo, int OrderNo)>
            {
                [101] = (1, 2), [102] = (1, 1), [103] = (null, 0),
            });
        var pool = new[]
        {
            P(1, swimmerId: 101), P(2, swimmerId: 102), P(3, swimmerId: 103), P(4), P(5, swimmerId: 999),
        };

        var dto = TrainingLaneView.Build(GroupLaneView.Plan, plan, 6, null, Levels, pool, 1, true)!;

        Assert.Equal((TrainingLaneView.SourcePlan, 2), (dto.Source, dto.LaneCount));
        Assert.Equal([2, 1], dto.Lanes[0].People.Select(p => p.UserId!.Value));   // порядок из плана
        Assert.Equal(("Fast", "10x100"), (dto.Lanes[0].Level!.Name, dto.Lanes[0].Workout!));
        Assert.Empty(dto.Lanes[1].People);
        Assert.Equal([3, 4, 5], dto.NoLane.Select(p => p.UserId!.Value));        // Unassigned, аккаунт, не в плане
    }

    // ── Имена и метки ───────────────────────────────────────────────────────

    [Fact]
    public void NamesHidden_OthersAnonymous_SelfShown()
    {
        var dto = Auto([P(1, swimmerId: 11, claims: 2), P(2, swimmerId: 12, claims: 2)], lanes: 1, viewer: 2, namesVisible: false);

        Assert.True(dto.NamesHidden);
        var people = dto.Lanes[0].People;
        var other = people.Single(p => !p.IsMe);
        Assert.Equal(((int?)null, (int?)null, (string?)null, (string?)null, 1),
            (other.UserId, other.SwimmerId, other.Name, other.Gender, other.Claims));
        var me = people.Single(p => p.IsMe);
        Assert.Equal((2, "U2", 1), (me.UserId!.Value, me.Name!, me.Claims));   // «2 claim» — только при видимых именах
    }

    // ── Сборка в ответе RSVP ────────────────────────────────────────────────

    private static readonly DateTime MondayNoon = new(2026, 9, 28, 12, 0, 0);
    private const string TueKey = "2026-09-29-2000";

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private sealed record Seed(int GroupId, int Coach, int Anna, int Boris, int Vera, int Dan,
        int AnnaSw, int BorisSw, int KidSw, int OutsideSw);

    /// <summary>
    /// Anna — метка тренера на AnnaSw (и «Me» на BorisSw — метка главнее); Boris — «Me» на BorisSw;
    /// Vera — «Family» на KidSw; Dan — «Me» на пловце вне состава → стоит аккаунтом.
    /// </summary>
    private static async Task<Seed> SeedAsync(SwimmDbContext db, GroupTrainingSchedule? schedule = null)
    {
        AppUser U(string name) => new() { Email = $"{name}@x", DisplayName = name, SecurityStamp = "s" };
        var coach = U("Coach"); var anna = U("Anna"); var boris = U("Boris"); var vera = U("Vera"); var dan = U("Dan");
        Swimmer S(string last, string gender) => new() { LastName = last, FirstName = "F", Gender = gender, BirthYear = 1980 };
        var annaSw = S("אנה", "female"); var borisSw = S("בוריס", "male"); var kidSw = S("ילד", "male"); var outside = S("חוץ", "male");
        schedule ??= new GroupTrainingSchedule();
        schedule.Slots = [new GroupTrainingSlot { Day = 2, Start = "20:00" }];
        var group = new HubGroup { Name = "G", Slug = "g", Owner = coach, TrainingSchedule = schedule.ToJson() };
        db.AddRange(coach, anna, boris, vera, dan, annaSw, borisSw, kidSw, outside, group);
        await db.SaveChangesAsync();

        db.HubGroupUserMembers.AddRange(
            new HubGroupUserMember { HubGroupId = group.Id, UserId = anna.Id, SwimmerId = annaSw.Id },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = boris.Id },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = vera.Id },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = dan.Id });
        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = kidSw.Id, SortOrder = 1 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = annaSw.Id, SortOrder = 2 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = borisSw.Id, SortOrder = 3 });
        UserFavorite F(AppUser u, Swimmer s, bool me, bool family) =>
            new() { UserId = u.Id, TargetType = "swimmer", SwimmerId = s.Id, IsPrimary = me, IsFamily = family };
        db.UserFavorites.AddRange(
            F(anna, borisSw, true, false), F(boris, borisSw, true, false),
            F(vera, kidSw, false, true), F(dan, outside, true, false));
        await db.SaveChangesAsync();
        return new Seed(group.Id, coach.Id, anna.Id, boris.Id, vera.Id, dan.Id, annaSw.Id, borisSw.Id, kidSw.Id, outside.Id);
    }

    private static async Task AnswerAllAsync(TrainingRsvpService rsvp, Seed s, string vera = "yes")
    {
        foreach (var (user, answer) in new[] { (s.Anna, "yes"), (s.Boris, "maybe"), (s.Vera, vera), (s.Dan, "yes") })
            await rsvp.SetAsync(s.GroupId, TueKey, user, false, new TrainingRsvpInputDto { Answer = answer }, MondayNoon);
    }

    [Fact]
    public async Task Resolver_LabelThenMeThenFamily_OutsideRosterStaysAccount()
    {
        await using var db = CreateDb(nameof(Resolver_LabelThenMeThenFamily_OutsideRosterStaysAccount));
        var s = await SeedAsync(db);

        var resolved = await HubGroupPersonResolver.ResolveAsync(db, s.GroupId, [s.Anna, s.Boris, s.Vera, s.Dan]);

        Assert.Equal(s.AnnaSw, resolved[s.Anna]);     // метка тренера главнее её «Me»
        Assert.Equal(s.BorisSw, resolved[s.Boris]);
        Assert.Equal(s.KidSw, resolved[s.Vera]);
        Assert.Null(resolved[s.Dan]);                 // «Me» вне состава — стоит аккаунтом
    }

    [Fact]
    public async Task Rsvp_WaterView_ComingInRosterOrder_AccountsAfter_NoIsOut()
    {
        await using var db = CreateDb(nameof(Rsvp_WaterView_ComingInRosterOrder_AccountsAfter_NoIsOut));
        var s = await SeedAsync(db);
        var rsvp = new TrainingRsvpService(db);
        await AnswerAllAsync(rsvp, s, vera: "no");

        var dto = (await rsvp.GetAsync(s.GroupId, TueKey, s.Anna, false, MondayNoon))!.LaneView!;

        Assert.Equal(TrainingLaneView.SourceWater, dto.Source);
        var people = Assert.Single(dto.Lanes).People;
        Assert.Equal([s.Anna, s.Boris, s.Dan], people.Select(p => p.UserId!.Value));   // Vera «не приду» — не в воде
        Assert.Equal(("אנה F", s.AnnaSw, true), (people[0].Name!, people[0].SwimmerId!.Value, people[0].IsMe));
        Assert.Equal(("maybe", "Dan", (int?)null), (people[1].Answer, people[2].Name!, people[2].SwimmerId));
    }

    [Fact]
    public async Task Rsvp_TwoAccountsOneSwimmer_BothShownWithClaims()
    {
        await using var db = CreateDb(nameof(Rsvp_TwoAccountsOneSwimmer_BothShownWithClaims));
        var s = await SeedAsync(db);
        // Метку Анны сняли — теперь её «Me» (Boris) спорит с «Me» Бориса.
        (await db.HubGroupUserMembers.SingleAsync(m => m.UserId == s.Anna)).SwimmerId = null;
        await db.SaveChangesAsync();
        var rsvp = new TrainingRsvpService(db);
        await AnswerAllAsync(rsvp, s);

        var people = (await rsvp.GetAsync(s.GroupId, TueKey, s.Coach, true, MondayNoon))!.LaneView!.Lanes[0].People;

        Assert.Equal(2, people.Count(p => p.SwimmerId == s.BorisSw && p.Claims == 2));
    }

    [Fact]
    public async Task Rsvp_AutoByLevels_SwimmerLevelThenAccountLevel()
    {
        await using var db = CreateDb(nameof(Rsvp_AutoByLevels_SwimmerLevelThenAccountLevel));
        var s = await SeedAsync(db, new GroupTrainingSchedule { UsualLanes = 2 });
        var fast = new HubGroupLevel { HubGroupId = s.GroupId, Rank = 1, Name = "Fast" };
        var slow = new HubGroupLevel { HubGroupId = s.GroupId, Rank = 2, Name = "Slow" };
        db.HubGroupLevels.AddRange(fast, slow);
        await db.SaveChangesAsync();
        db.HubGroupSwimmerLevels.Add(new HubGroupSwimmerLevel { HubGroupId = s.GroupId, SwimmerId = s.AnnaSw, LevelId = fast.Id });
        db.HubGroupAccountLevels.AddRange(
            new HubGroupAccountLevel { HubGroupId = s.GroupId, UserId = s.Anna, LevelId = slow.Id },  // у пловца свой — главнее
            new HubGroupAccountLevel { HubGroupId = s.GroupId, UserId = s.Dan, LevelId = slow.Id });
        await db.SaveChangesAsync();
        var rsvp = new TrainingRsvpService(db);
        await AnswerAllAsync(rsvp, s);

        var dto = (await rsvp.GetAsync(s.GroupId, TueKey, s.Coach, true, MondayNoon))!.LaneView!;

        Assert.Equal(TrainingLaneView.SourceAuto, dto.Source);
        Assert.Equal(("Fast", "Slow"), (dto.Lanes[0].Level!.Name, dto.Lanes[1].Level!.Name));
        Assert.Equal([s.Anna], dto.Lanes[0].People.Select(p => p.UserId!.Value));
        Assert.Equal([s.Dan], dto.Lanes[1].People.Select(p => p.UserId!.Value));
        Assert.Equal([s.Vera, s.Boris], dto.NoLane.Select(p => p.UserId!.Value));   // без уровня, порядок состава
    }

    [Fact]
    public async Task Rsvp_PublishedPlanWins_DraftIgnored()
    {
        await using var db = CreateDb(nameof(Rsvp_PublishedPlanWins_DraftIgnored));
        var s = await SeedAsync(db, new GroupTrainingSchedule { UsualLanes = 4 });
        var plan = new LanePlan
        {
            HubGroupId = s.GroupId, Date = new DateOnly(2026, 9, 29), LaneCount = 2, Status = LanePlanStatus.Draft,
            Lanes = [new LanePlanLane { LaneNo = 1 }, new LanePlanLane { LaneNo = 2, Workout = "kick" }],
            Swimmers = [new LanePlanSwimmer { SwimmerId = s.KidSw, LaneNo = 2 }],
        };
        db.LanePlans.Add(plan);
        await db.SaveChangesAsync();
        var rsvp = new TrainingRsvpService(db);
        await AnswerAllAsync(rsvp, s);

        Assert.Equal(TrainingLaneView.SourceAuto, (await rsvp.GetAsync(s.GroupId, TueKey, s.Vera, false, MondayNoon))!.LaneView!.Source);

        plan.Status = LanePlanStatus.Published;
        await db.SaveChangesAsync();
        var dto = (await rsvp.GetAsync(s.GroupId, TueKey, s.Vera, false, MondayNoon))!.LaneView!;

        Assert.Equal((TrainingLaneView.SourcePlan, "kick"), (dto.Source, dto.Lanes[1].Workout!));
        Assert.Equal([s.Vera], dto.Lanes[1].People.Select(p => p.UserId!.Value));    // Vera стоит KidSw по «Family»
        Assert.Equal([s.Anna, s.Boris, s.Dan], dto.NoLane.Select(p => p.UserId!.Value));
    }

    [Fact]
    public async Task Rsvp_WhoIsComingCoach_MemberSeesOnlySelf_CoachSeesAll_OffHidesView()
    {
        await using var db = CreateDb(nameof(Rsvp_WhoIsComingCoach_MemberSeesOnlySelf_CoachSeesAll_OffHidesView));
        var s = await SeedAsync(db, new GroupTrainingSchedule { WhoIsComing = GroupWhoIsComing.Coach });
        var rsvp = new TrainingRsvpService(db);
        await AnswerAllAsync(rsvp, s);

        var member = (await rsvp.GetAsync(s.GroupId, TueKey, s.Dan, false, MondayNoon))!.LaneView!;
        Assert.True(member.NamesHidden);
        Assert.Equal(["Dan"], member.Lanes[0].People.Where(p => p.Name != null).Select(p => p.Name!));
        Assert.Equal(4, member.Lanes[0].People.Count);

        var coach = (await rsvp.GetAsync(s.GroupId, TueKey, s.Coach, true, MondayNoon))!.LaneView!;
        Assert.False(coach.NamesHidden);
        Assert.All(coach.Lanes[0].People, p => Assert.NotNull(p.Name));

        var group = await db.HubGroups.SingleAsync();
        group.TrainingSchedule = new GroupTrainingSchedule
        {
            Slots = [new GroupTrainingSlot { Day = 2, Start = "20:00" }], LaneView = GroupLaneView.Off,
        }.ToJson();
        await db.SaveChangesAsync();
        Assert.Null((await rsvp.GetAsync(s.GroupId, TueKey, s.Coach, true, MondayNoon))!.LaneView);
    }

    [Fact]
    public async Task Break_LinkedByMe_PutsTheAccountOnBreak()
    {
        await using var db = CreateDb(nameof(Break_LinkedByMe_PutsTheAccountOnBreak));
        var s = await SeedAsync(db);
        db.HubGroupBreaks.Add(new HubGroupBreak { HubGroupId = s.GroupId, SwimmerId = s.BorisSw, SetByUserId = s.Coach });
        await db.SaveChangesAsync();

        var dto = (await new TrainingRsvpService(db).GetAsync(s.GroupId, TueKey, s.Coach, true, MondayNoon))!;

        Assert.True(dto.People!.Single(p => p.UserId == s.Boris).OnBreak);   // связка через «Me»
        Assert.False(dto.People!.Single(p => p.UserId == s.Anna).OnBreak);  // у Анны метка на другого
    }
}
