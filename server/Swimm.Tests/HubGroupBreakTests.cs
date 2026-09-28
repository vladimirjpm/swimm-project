using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Ш3.1 (docs/plans/entity-hero-roles-plan.md §5): флаг «On break» (<see cref="HubGroupBreakRules"/>,
/// <see cref="HubGroupBreakService"/>) и его след в RSVP и плане дорожек, уровень аккаунта без
/// пловца, «Usual lanes» / «Lane view» в расписании, перенос перерыва при склейке пловцов.
/// Решения Влада 28.09.2026: флаг один на человека; тренер ставит любому и бессрочно, участник —
/// себе и до даты; «Going» снимает перерыв с пометкой тренеру; доступа к группе флаг не отнимает.
/// </summary>
public class HubGroupBreakTests
{
    // Вторник 29.09.2026 — ближайшее занятие; «сейчас» — понедельник 28.09, полдень.
    private static readonly DateOnly Today = new(2026, 9, 28);
    private static readonly DateTime MondayNoon = new(2026, 9, 28, 12, 0, 0);
    private static readonly DateTime MondayNoonUtc = new(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);
    private const string TueKey = "2026-09-29-2000";

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed record Seed(int GroupId, int Coach, int Anna, int Boris, int Vera,
        int AnnaSwimmer, int BorisSwimmer, int Gil);

    /// <summary>
    /// Группа с расписанием «каждый вторник 20:00»; аккаунты Anna (метка тренера → пловчиха
    /// Anna), Boris (метка → пловец Boris), Vera (без пловца); Gil — пловец состава без аккаунта;
    /// Coach управляет группой, в составе не состоит.
    /// </summary>
    private static async Task<Seed> SeedAsync(SwimmDbContext db)
    {
        AppUser U(string name) => new() { Email = $"{name}@example.com", DisplayName = name, SecurityStamp = "s" };
        var coach = U("Coach");
        var anna = U("Anna");
        var boris = U("Boris");
        var vera = U("Vera");
        var annaSwimmer = new Swimmer { LastName = "A", FirstName = "Anna", Gender = "female", BirthYear = 1980 };
        var borisSwimmer = new Swimmer { LastName = "B", FirstName = "Boris", Gender = "male", BirthYear = 1980 };
        var gil = new Swimmer { LastName = "G", FirstName = "Gil", Gender = "male", BirthYear = 1975 };
        var schedule = new GroupTrainingSchedule
        {
            Slots = [new GroupTrainingSlot { Day = 2, Start = "20:00", End = "21:00" }],
        };
        var group = new HubGroup { Name = "Dolphins", Slug = "dolphins", Owner = coach, TrainingSchedule = schedule.ToJson() };
        db.AddRange(coach, anna, boris, vera, annaSwimmer, borisSwimmer, gil, group);
        await db.SaveChangesAsync();

        db.HubGroupUserMembers.AddRange(
            new HubGroupUserMember { HubGroupId = group.Id, UserId = anna.Id, SwimmerId = annaSwimmer.Id },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = boris.Id, SwimmerId = borisSwimmer.Id },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = vera.Id });
        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = annaSwimmer.Id, SortOrder = 1 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = borisSwimmer.Id, SortOrder = 2 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = gil.Id, SortOrder = 3 });
        await db.SaveChangesAsync();
        return new Seed(group.Id, coach.Id, anna.Id, boris.Id, vera.Id, annaSwimmer.Id, borisSwimmer.Id, gil.Id);
    }

    private static HubGroupBreakInputDto Break(bool on, string? until = null, int? userId = null, int? swimmerId = null) =>
        new() { OnBreak = on, Until = until, UserId = userId, SwimmerId = swimmerId };

    // ── Правила ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsActive_UntilIsInclusive_EndedOrExpiredIsNot()
    {
        Assert.True(HubGroupBreakRules.IsActive(null, null, Today));
        Assert.True(HubGroupBreakRules.IsActive(null, Today, Today));
        Assert.False(HubGroupBreakRules.IsActive(null, Today.AddDays(-1), Today));
        Assert.False(HubGroupBreakRules.IsActive(DateTime.UtcNow, null, Today));
    }

    [Fact]
    public void ValidateUntil_SelfNeedsDate_CoachMayGoOpenEnded()
    {
        Assert.NotNull(HubGroupBreakRules.ValidateUntil(null, Today, bySelf: true));
        Assert.Null(HubGroupBreakRules.ValidateUntil(null, Today, bySelf: false));
        Assert.Null(HubGroupBreakRules.ValidateUntil(Today, Today, bySelf: true));
        Assert.NotNull(HubGroupBreakRules.ValidateUntil(Today.AddDays(-1), Today, bySelf: false));
        Assert.NotNull(HubGroupBreakRules.ValidateUntil(Today.AddDays(HubGroupBreakRules.MaxDaysAhead + 1), Today, bySelf: false));
    }

    [Fact]
    public void BackAfterDays_OnlyForRsvpReturns_ForTwoWeeks()
    {
        var since = MondayNoonUtc.AddDays(-150);
        var ended = MondayNoonUtc.AddDays(-1);

        Assert.Equal(149, HubGroupBreakRules.BackAfterDays(since, ended, endedByRsvp: true, MondayNoonUtc));
        Assert.Null(HubGroupBreakRules.BackAfterDays(since, ended, endedByRsvp: false, MondayNoonUtc));
        Assert.Null(HubGroupBreakRules.BackAfterDays(since, MondayNoonUtc.AddDays(-15), endedByRsvp: true, MondayNoonUtc));
    }

    // ── Сервис перерыва ─────────────────────────────────────────────────────

    [Fact]
    public async Task Member_TakesBreakUntilDate_CoachSeesIt()
    {
        await using var db = CreateDb(nameof(Member_TakesBreakUntilDate_CoachSeesIt));
        var s = await SeedAsync(db);
        var service = new HubGroupBreakService(db);

        var noDate = await service.SetAsync(s.GroupId, s.Vera, false, Break(true), MondayNoonUtc);
        Assert.Equal(400, noDate.Status);

        var result = await service.SetAsync(s.GroupId, s.Vera, false, Break(true, "2026-10-31"), MondayNoonUtc);

        var mine = Assert.IsType<HubGroupBreaksDto>(result.Breaks).Mine!;
        Assert.Equal(("2026-10-31", false), (mine.Until, mine.SetByCoach));
        Assert.Null(result.Breaks!.Breaks);                  // список — только тренеру
        var coachView = await service.GetAsync(s.GroupId, s.Coach, true, MondayNoonUtc);
        Assert.Equal([("Vera", "2026-10-31")], coachView.Breaks!.Select(b => (b.Name, b.Until!)));
    }

    [Fact]
    public async Task MemberCannotSetForOthers_CoachCanForSwimmerOpenEnded()
    {
        await using var db = CreateDb(nameof(MemberCannotSetForOthers_CoachCanForSwimmerOpenEnded));
        var s = await SeedAsync(db);
        var service = new HubGroupBreakService(db);

        Assert.Equal(403, (await service.SetAsync(s.GroupId, s.Vera, false, Break(true, "2026-10-31", userId: s.Anna), MondayNoonUtc)).Status);
        Assert.Equal(403, (await service.SetAsync(s.GroupId, s.Vera, false, Break(true, "2026-10-31", swimmerId: s.Gil), MondayNoonUtc)).Status);
        Assert.Equal(400, (await service.SetAsync(s.GroupId, s.Coach, true, Break(true, userId: s.Coach), MondayNoonUtc)).Status); // тренер вне состава

        var gil = await service.SetAsync(s.GroupId, s.Coach, true, Break(true, swimmerId: s.Gil), MondayNoonUtc);

        var row = Assert.Single(gil.Breaks!.Breaks!);
        Assert.Equal((s.Gil, (string?)null, true), (row.SwimmerId!.Value, row.Until, row.SetByCoach));
    }

    [Fact]
    public async Task SwitchOff_EndsTheBreak_AndReSetReusesTheRow()
    {
        await using var db = CreateDb(nameof(SwitchOff_EndsTheBreak_AndReSetReusesTheRow));
        var s = await SeedAsync(db);
        var service = new HubGroupBreakService(db);
        await service.SetAsync(s.GroupId, s.Vera, false, Break(true, "2026-09-30"), MondayNoonUtc);

        // Срок истёк сам — строка открыта, но не действует; новый перерыв её переиспользует.
        var later = MondayNoonUtc.AddDays(5);
        Assert.Null((await service.GetAsync(s.GroupId, s.Vera, false, later)).Mine);
        await service.SetAsync(s.GroupId, s.Vera, false, Break(true, "2026-10-20"), later);
        Assert.Equal(1, await db.HubGroupBreaks.CountAsync());

        var off = await service.SetAsync(s.GroupId, s.Vera, false, Break(false), later);

        Assert.Null(off.Breaks!.Mine);
        Assert.NotNull((await db.HubGroupBreaks.SingleAsync()).EndedAt);
    }

    // ── RSVP ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rsvp_OnBreakWithoutAnswer_LeavesTheDenominator_AnsweredStays()
    {
        await using var db = CreateDb(nameof(Rsvp_OnBreakWithoutAnswer_LeavesTheDenominator_AnsweredStays));
        var s = await SeedAsync(db);
        await new HubGroupBreakService(db).SetAsync(s.GroupId, s.Coach, true, Break(true, userId: s.Vera), MondayNoonUtc);
        var rsvp = new TrainingRsvpService(db);

        var before = (await rsvp.GetAsync(s.GroupId, TueKey, s.Coach, true, MondayNoon))!;
        Assert.Equal(2, before.Total);                               // Vera на перерыве и молчит
        var vera = before.People!.Single(p => p.UserId == s.Vera);
        Assert.True(vera.OnBreak);
        Assert.Equal(s.Vera, before.People!.Last().UserId);          // в самом конце списка

        // «Не приду» на перерыве — ответ как у всех, в знаменателе он есть.
        var after = (await rsvp.SetAsync(s.GroupId, TueKey, s.Vera, false, new TrainingRsvpInputDto { Answer = "no" }, MondayNoon)).Rsvp!;
        Assert.Equal((3, 1), (after.Total, after.No));
        Assert.True(after.OnBreak);
    }

    [Fact]
    public async Task Rsvp_GoingDuringBreak_EndsIt_CoachSeesBackAfter()
    {
        await using var db = CreateDb(nameof(Rsvp_GoingDuringBreak_EndsIt_CoachSeesBackAfter));
        var s = await SeedAsync(db);
        db.HubGroupBreaks.Add(new HubGroupBreak
        {
            HubGroupId = s.GroupId, UserId = s.Vera, Since = DateTime.UtcNow.AddDays(-150), SetByUserId = s.Coach,
        });
        await db.SaveChangesAsync();
        var rsvp = new TrainingRsvpService(db);

        await rsvp.SetAsync(s.GroupId, TueKey, s.Vera, false, new TrainingRsvpInputDto { Answer = "yes" }, MondayNoon);

        var row = await db.HubGroupBreaks.SingleAsync();
        Assert.True(row.EndedByRsvp);
        Assert.NotNull(row.EndedAt);
        var coach = (await rsvp.GetAsync(s.GroupId, TueKey, s.Coach, true, MondayNoon))!;
        var vera = coach.People!.Single(p => p.UserId == s.Vera);
        Assert.False(vera.OnBreak);
        Assert.InRange(vera.BackAfterDays!.Value, 149, 150);
        var returns = (await new HubGroupBreakService(db).GetAsync(s.GroupId, s.Coach, true)).Returns!;
        Assert.Equal("Vera", Assert.Single(returns).Name);
    }

    [Fact]
    public async Task Rsvp_GoingAfterTheBreakEnds_DoesNotTouchIt()
    {
        await using var db = CreateDb(nameof(Rsvp_GoingAfterTheBreakEnds_DoesNotTouchIt));
        var s = await SeedAsync(db);
        db.HubGroupBreaks.Add(new HubGroupBreak
        {
            HubGroupId = s.GroupId, UserId = s.Vera, Until = new DateOnly(2026, 9, 28), SetByUserId = s.Vera,
        });
        await db.SaveChangesAsync();

        await new TrainingRsvpService(db).SetAsync(s.GroupId, TueKey, s.Vera, false, new TrainingRsvpInputDto { Answer = "yes" }, MondayNoon);

        Assert.Null((await db.HubGroupBreaks.SingleAsync()).EndedAt);
    }

    [Fact]
    public async Task OneFlagPerPerson_SwimmerBreakPutsHisAccountOnBreak_AndGoingEndsBoth()
    {
        await using var db = CreateDb(nameof(OneFlagPerPerson_SwimmerBreakPutsHisAccountOnBreak_AndGoingEndsBoth));
        var s = await SeedAsync(db);
        await new HubGroupBreakService(db).SetAsync(s.GroupId, s.Coach, true, Break(true, swimmerId: s.BorisSwimmer), MondayNoonUtc);
        var rsvp = new TrainingRsvpService(db);

        var dto = (await rsvp.GetAsync(s.GroupId, TueKey, s.Coach, true, MondayNoon))!;
        Assert.True(dto.People!.Single(p => p.UserId == s.Boris).OnBreak);   // метка тренера связывает
        Assert.Equal(2, dto.Total);

        await rsvp.SetAsync(s.GroupId, TueKey, s.Boris, false, new TrainingRsvpInputDto { Answer = "yes" }, MondayNoon);

        Assert.True((await db.HubGroupBreaks.SingleAsync()).EndedByRsvp);
    }

    // ── План дорожек ────────────────────────────────────────────────────────

    [Fact]
    public async Task LanePlan_DefaultPresentSkipsOnBreak_NamedStays()
    {
        await using var db = CreateDb(nameof(LanePlan_DefaultPresentSkipsOnBreak_NamedStays));
        var s = await SeedAsync(db);
        await new HubGroupBreakService(db).SetAsync(s.GroupId, s.Coach, true, Break(true, swimmerId: s.Gil), DateTime.UtcNow);
        var plans = new LanePlanService(db);
        LanePlanDistributeInputDto Input(List<int>? ids) => new()
        {
            LaneCount = 1, Lanes = [new LanePlanLaneInputDto { LaneNo = 1 }], SwimmerIds = ids,
        };

        var byDefault = (await plans.DistributeAsync(s.GroupId, Input(null))).Result!;
        Assert.Equal([s.AnnaSwimmer, s.BorisSwimmer], byDefault.Swimmers.Select(w => w.SwimmerId).Order());

        // Тренер назвал руками — его решение, не фильтруем.
        var named = (await plans.DistributeAsync(s.GroupId, Input([s.Gil]))).Result!;
        Assert.Equal([s.Gil], named.Swimmers.Select(w => w.SwimmerId));
    }

    [Fact]
    public async Task LanePlan_BoardMarksOnBreakOnPlanDate()
    {
        await using var db = CreateDb(nameof(LanePlan_BoardMarksOnBreakOnPlanDate));
        var s = await SeedAsync(db);
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        db.HubGroupBreaks.Add(new HubGroupBreak { HubGroupId = s.GroupId, SwimmerId = s.Gil, Until = day, SetByUserId = s.Coach });
        await db.SaveChangesAsync();
        var plans = new LanePlanService(db);
        Assert.True((await plans.SaveAsync(s.GroupId, day, new LanePlanInputDto
        {
            LaneCount = 1,
            Lanes = [new LanePlanLaneInputDto { LaneNo = 1 }],
            Swimmers = [new LanePlanSwimmerInputDto { SwimmerId = s.AnnaSwimmer, LaneNo = 1 }],
        }, s.Coach)).Success);

        var board = (await plans.GetAsync(s.GroupId, day, isManager: true))!;

        Assert.True(board.NotToday.Single(w => w.SwimmerId == s.Gil).OnBreak);
        Assert.False(board.Lanes[0].Swimmers.Single().OnBreak);
        // На следующий день после конца перерыва Gil уже не на перерыве.
        await plans.SaveAsync(s.GroupId, day.AddDays(1), new LanePlanInputDto
        {
            LaneCount = 1, Lanes = [new LanePlanLaneInputDto { LaneNo = 1 }], Swimmers = [],
        }, s.Coach);
        var next = (await plans.GetAsync(s.GroupId, day.AddDays(1), isManager: true))!;
        Assert.False(next.NotToday.Single(w => w.SwimmerId == s.Gil).OnBreak);
    }

    // ── Уровень аккаунта ────────────────────────────────────────────────────

    [Fact]
    public async Task AccountLevel_OnlyActiveMember_ShownAndRemovedWithTheLevel()
    {
        await using var db = CreateDb(nameof(AccountLevel_OnlyActiveMember_ShownAndRemovedWithTheLevel));
        var s = await SeedAsync(db);
        var levels = new HubGroupLevelService(db);
        var dto = (await levels.GetAsync(s.GroupId))!;              // заводит стандартные уровни
        var top = dto.Levels[0].Id;

        Assert.False((await levels.SetAccountLevelAsync(s.GroupId, s.Coach, top)).Success);
        Assert.True((await levels.SetAccountLevelAsync(s.GroupId, s.Vera, top)).Success);

        var after = (await levels.GetAsync(s.GroupId))!;
        var vera = after.Accounts.Single(a => a.UserId == s.Vera);
        Assert.Equal((top, (int?)null), (vera.LevelId!.Value, vera.SwimmerId));
        Assert.Equal(s.AnnaSwimmer, after.Accounts.Single(a => a.UserId == s.Anna).SwimmerId);
        Assert.Equal(1, after.Levels[0].AccountCount);

        // Уровень удалили — аккаунт «без уровня».
        var keep = after.Levels.Skip(1).Select(l => new HubGroupLevelInputDto { Id = l.Id, Name = l.Name }).ToList();
        Assert.True((await levels.SaveLevelsAsync(s.GroupId, new HubGroupLevelsInputDto { Levels = keep })).Success);
        Assert.False(await db.HubGroupAccountLevels.AnyAsync());

        Assert.True((await levels.SetAccountLevelAsync(s.GroupId, s.Vera, after.Levels[1].Id)).Success);
        Assert.True((await levels.SetAccountLevelAsync(s.GroupId, s.Vera, null)).Success);
        Assert.False(await db.HubGroupAccountLevels.AnyAsync());
    }

    // ── Расписание: Usual lanes и Lane view ─────────────────────────────────

    [Fact]
    public async Task Schedule_UsualLanesAndLaneView_ValidatedAndStored()
    {
        await using var db = CreateDb(nameof(Schedule_UsualLanesAndLaneView_ValidatedAndStored));
        var s = await SeedAsync(db);
        var users = new HubGroupUserService(db, new HubGroupCrudCore(db), new StubSettings());
        GroupTrainingScheduleDto Dto(int? lanes, string? view) => new()
        {
            Slots = [new GroupTrainingSlotDto { Day = 2, Start = "20:00" }], UsualLanes = lanes, LaneView = view,
        };

        Assert.False((await users.SetTrainingScheduleAsync(s.GroupId, Dto(13, null))).Success);
        Assert.False((await users.SetTrainingScheduleAsync(s.GroupId, Dto(0, null))).Success);
        Assert.False((await users.SetTrainingScheduleAsync(s.GroupId, Dto(4, "banana"))).Success);

        Assert.True((await users.SetTrainingScheduleAsync(s.GroupId, Dto(4, "OFF"))).Success);
        var stored = GroupTrainingSchedule.Parse((await db.HubGroups.SingleAsync()).TrainingSchedule);
        Assert.Equal((4, "off", "off"), (stored.UsualLanes!.Value, stored.LaneView!, stored.EffectiveLaneView));

        // auto — по умолчанию: хранится как отсутствие ключа.
        Assert.True((await users.SetTrainingScheduleAsync(s.GroupId, Dto(null, "auto"))).Success);
        var auto = GroupTrainingSchedule.Parse((await db.HubGroups.SingleAsync()).TrainingSchedule);
        Assert.Equal(((int?)null, (string?)null, "auto"), (auto.UsualLanes, auto.LaneView, auto.EffectiveLaneView));
        Assert.DoesNotContain("lane_view", (await db.HubGroups.SingleAsync()).TrainingSchedule);
    }

    private sealed class StubSettings : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }

    // ── Склейка пловцов ─────────────────────────────────────────────────────

    [Fact]
    public async Task Merge_MovesTheBreakToCanonical_ClosesDuplicateIfBothOpen()
    {
        await using var db = CreateDb(nameof(Merge_MovesTheBreakToCanonical_ClosesDuplicateIfBothOpen));
        var s = await SeedAsync(db);
        var dup = new Swimmer { LastName = "G", FirstName = "Gil", Gender = "male", BirthYear = 1975 };
        db.Swimmers.Add(dup);
        await db.SaveChangesAsync();
        db.HubGroupBreaks.AddRange(
            new HubGroupBreak { HubGroupId = s.GroupId, SwimmerId = s.Gil, SetByUserId = s.Coach },
            new HubGroupBreak { HubGroupId = s.GroupId, SwimmerId = dup.Id, SetByUserId = s.Coach });
        await db.SaveChangesAsync();

        await new SwimmerMergeService(db).MergeAsync([new SwimmerMergePair(s.Gil, dup.Id)], dryRun: false);

        var rows = await db.HubGroupBreaks.ToListAsync();
        Assert.All(rows, r => Assert.Equal(s.Gil, r.SwimmerId));
        Assert.Single(rows, r => r.EndedAt == null);
    }
}
