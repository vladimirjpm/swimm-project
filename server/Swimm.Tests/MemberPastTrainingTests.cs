using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Swimm.API.Controllers;
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
/// Прошедшие тренировки глазами участника (решение Влада 26.11.2026, отменяет «участнику прошедших не
/// показываем» CF9, docs/plans/trainings-coach-flow-plan.md): список «Past» — только занятия с
/// опубликованным планом за <see cref="TrainingRsvpRules.PastDaysBack"/> дней, без счётчиков; в занятии —
/// все дорожки плана и workout каждого уровня, своя дорожка — по плану. Права на запись не меняются.
/// </summary>
public class MemberPastTrainingTests
{
    /// <summary>Четверг 26.11.2026, полдень по Израилю. Окно 90 дней — с 28.08.</summary>
    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0);

    private static readonly DateOnly Published = new(2026, 10, 27);   // вт, опубликованный план
    private static readonly DateOnly Draft = new(2026, 11, 3);        // вт, черновик
    private static readonly DateOnly AnswersOnly = new(2026, 11, 10); // вт, только ответы
    private static readonly DateOnly TooOld = new(2026, 7, 28);       // вт, опубликован, но > 90 дней
    private const string PublishedKey = "2026-10-27-2000";

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    /// <summary>
    /// Вторник 20:00. Anna — аккаунт пловчихи Anna (Fast), Boris — пловца Boris (Slow), Vera — аккаунт
    /// без пловца. Опубликованный план 27.10: дорожка 1 Fast (Anna), дорожка 2 Slow (Boris); в workout
    /// по сету на каждый уровень.
    /// </summary>
    private sealed record Seed(int GroupId, int Coach, int Anna, int Boris, int Vera, int Outsider, int Fast, int Slow);

    private static async Task<Seed> SeedAsync(SwimmDbContext db)
    {
        AppUser U(string name) => new() { Email = $"{name}@example.com", DisplayName = name, SecurityStamp = "s" };
        var coach = U("Coach");
        var anna = U("Anna");
        var boris = U("Boris");
        var vera = U("Vera");
        var outsider = U("Outsider");
        var annaSwimmer = new Swimmer { LastName = "A", FirstName = "Anna", Gender = "female", BirthYear = 1980 };
        var borisSwimmer = new Swimmer { LastName = "B", FirstName = "Boris", Gender = "male", BirthYear = 1980 };
        var schedule = new GroupTrainingSchedule { Slots = [new GroupTrainingSlot { Day = 2, Start = "20:00", End = "21:00" }] };
        var group = new HubGroup { Name = "Dolphins", Slug = "dolphins", Owner = coach, TrainingSchedule = schedule.ToJson() };
        db.AddRange(coach, anna, boris, vera, outsider, annaSwimmer, borisSwimmer, group);
        await db.SaveChangesAsync();

        var fast = new HubGroupLevel { HubGroupId = group.Id, Rank = 1, Name = "Fast" };
        var slow = new HubGroupLevel { HubGroupId = group.Id, Rank = 2, Name = "Slow" };
        db.HubGroupLevels.AddRange(fast, slow);
        db.HubGroupUserMembers.AddRange(
            new HubGroupUserMember { HubGroupId = group.Id, UserId = anna.Id, SwimmerId = annaSwimmer.Id },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = boris.Id, SwimmerId = borisSwimmer.Id },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = vera.Id });
        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = annaSwimmer.Id, SortOrder = 1 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = borisSwimmer.Id, SortOrder = 2 });
        await db.SaveChangesAsync();
        db.HubGroupSwimmerLevels.AddRange(
            new HubGroupSwimmerLevel { HubGroupId = group.Id, SwimmerId = annaSwimmer.Id, LevelId = fast.Id },
            new HubGroupSwimmerLevel { HubGroupId = group.Id, SwimmerId = borisSwimmer.Id, LevelId = slow.Id });

        var workout = WorkoutRules.Serialize(new WorkoutDto
        {
            Sets =
            [
                new WorkoutSetDto { Title = "Fast set", LevelIds = [fast.Id], Lines = [new WorkoutLineDto { Reps = 8, Distance = 100 }] },
                new WorkoutSetDto { Title = "Slow set", LevelIds = [slow.Id], Lines = [new WorkoutLineDto { Reps = 4, Distance = 100 }] },
            ],
        });
        LanePlan Plan(DateOnly date, string status) => new()
        {
            HubGroupId = group.Id, Date = date, Start = "20:00", LaneCount = 2, Status = status, Workout = workout,
            Lanes = [new LanePlanLane { LaneNo = 1, LevelId = fast.Id }, new LanePlanLane { LaneNo = 2, LevelId = slow.Id }],
            Swimmers =
            [
                new LanePlanSwimmer { SwimmerId = annaSwimmer.Id, LaneNo = 1 },
                new LanePlanSwimmer { SwimmerId = borisSwimmer.Id, LaneNo = 2 },
            ],
        };
        db.LanePlans.AddRange(
            Plan(Published, LanePlanStatus.Published),
            Plan(Draft, LanePlanStatus.Draft),
            Plan(TooOld, LanePlanStatus.Published));
        // Boris ответил «Going» на занятие с планом и на занятие без плана; Anna не отвечала нигде.
        db.HubGroupTrainingRsvps.AddRange(
            new HubGroupTrainingRsvp { HubGroupId = group.Id, SessionDate = Published, SessionStart = "20:00", UserId = boris.Id, Answer = "yes" },
            new HubGroupTrainingRsvp { HubGroupId = group.Id, SessionDate = AnswersOnly, SessionStart = "20:00", UserId = boris.Id, Answer = "yes" });
        await db.SaveChangesAsync();
        return new Seed(group.Id, coach.Id, anna.Id, boris.Id, vera.Id, outsider.Id, fast.Id, slow.Id);
    }

    // ── Правило окна ────────────────────────────────────────────────────────

    [Fact]
    public void IsMemberPast_StartedAndWithinPastDaysBack()
    {
        var schedule = new GroupTrainingSchedule { Slots = [new GroupTrainingSlot { Day = 2, Start = "20:00", End = "21:00" }] };
        TrainingOccurrences.Occurrence? Occ(DateOnly date) => TrainingOccurrences.Find(schedule, [], [], date, "20:00");
        var today = DateOnly.FromDateTime(Now);

        Assert.True(TrainingRsvpRules.IsMemberPast(Occ(Published), Now));
        var edge = today.AddDays(-TrainingRsvpRules.PastDaysBack);
        while (edge.DayOfWeek != DayOfWeek.Tuesday) edge = edge.AddDays(1);
        Assert.True(TrainingRsvpRules.IsMemberPast(Occ(edge), Now));
        Assert.False(TrainingRsvpRules.IsMemberPast(Occ(edge.AddDays(-7)), Now));   // глубже 90 дней
        Assert.False(TrainingRsvpRules.IsMemberPast(Occ(new DateOnly(2026, 12, 1)), Now));   // впереди
        Assert.False(TrainingRsvpRules.IsMemberPast(null, Now));

        // Началось — уже прошедшее (как «начавшееся — не ближайшее»); до начала — ещё нет.
        var tue = new DateOnly(2026, 11, 24);
        Assert.False(TrainingRsvpRules.IsMemberPast(Occ(tue), tue.ToDateTime(new TimeOnly(19, 59))));
        Assert.True(TrainingRsvpRules.IsMemberPast(Occ(tue), tue.ToDateTime(new TimeOnly(20, 0))));

        // Смотреть — не править: окно ответа участника не открылось.
        Assert.NotNull(TrainingRsvpRules.EditBlockReason(Occ(Published), Now, isManager: false));
    }

    // ── Список «Past» ───────────────────────────────────────────────────────

    [Fact]
    public async Task MemberPast_OnlyPublishedPlans_Within90Days_NoCounts()
    {
        await using var db = CreateDb(nameof(MemberPast_OnlyPublishedPlans_Within90Days_NoCounts));
        var s = await SeedAsync(db);
        var service = new TrainingRsvpService(db);

        var member = await service.GetMemberPastAsync(s.GroupId, Now);
        var only = Assert.Single(member!);
        Assert.Equal(PublishedKey, only.Session.Id);
        Assert.Equal((null, null, null), (only.Yes, only.Maybe, only.No));   // без счётчиков

        // Тренеру — как раньше: черновик и занятие только с ответами тоже, со счётчиками.
        var coach = await service.GetPastAsync(s.GroupId, Now);
        Assert.Equal(["2026-11-10-2000", "2026-11-03-2000", PublishedKey], coach!.Select(p => p.Session.Id));
        Assert.Equal(1, coach[2].Yes);

        Assert.Null(await service.GetMemberPastAsync(12345, Now));
    }

    // ── Прошедшее занятие ───────────────────────────────────────────────────

    [Fact]
    public async Task MemberOpensPast_AllLanes_WorkoutOfEveryLevel_OwnLaneByPlan_ReadOnly()
    {
        await using var db = CreateDb(nameof(MemberOpensPast_AllLanes_WorkoutOfEveryLevel_OwnLaneByPlan_ReadOnly));
        var s = await SeedAsync(db);
        var service = new TrainingRsvpService(db);

        // Anna не отвечала, занятию месяц — глубже недели, но план опубликован.
        var dto = await service.GetAsync(s.GroupId, PublishedKey, s.Anna, false, Now);

        Assert.NotNull(dto);
        var view = dto!.LaneView!;
        Assert.Equal(TrainingLaneView.SourcePlan, view.Source);
        Assert.Equal([1, 2], view.Lanes.Select(l => l.LaneNo));
        Assert.Equal("Fast set", Assert.Single(view.Lanes[0].LevelWorkout!.Sets).Title);
        Assert.Equal("Slow set", Assert.Single(view.Lanes[1].LevelWorkout!.Sets).Title);
        // Своя дорожка — по плану, хотя ответа нет.
        var me = Assert.Single(view.Lanes[0].People);
        Assert.True(me.IsMe);
        Assert.Equal("", me.Answer);
        // Boris — в бассейне по ответу, как всегда.
        Assert.Equal("yes", Assert.Single(view.Lanes[1].People).Answer);

        Assert.False(dto.CanAnswer);
        Assert.False(dto.CanManage);
        Assert.Null(dto.People);   // перекличка — только управляющему
        Assert.Null(dto.Needs);

        // Ответить на прошедшее участник по-прежнему не может.
        var set = await service.SetAsync(s.GroupId, PublishedKey, s.Anna, false, new TrainingRsvpInputDto { Answer = "yes" }, Now);
        Assert.Equal(400, set.Status);
    }

    [Fact]
    public async Task MemberPast_DraftOrNoPlanBeyondWeek_OrTooOld_IsNotFound_CoachStillSees()
    {
        await using var db = CreateDb(nameof(MemberPast_DraftOrNoPlanBeyondWeek_OrTooOld_IsNotFound_CoachStillSees));
        var s = await SeedAsync(db);
        var service = new TrainingRsvpService(db);

        // 03.11 (черновик) и 10.11 (только ответы) — старше недели: участнику нет.
        Assert.Null(await service.GetAsync(s.GroupId, "2026-11-03-2000", s.Anna, false, Now));
        Assert.Null(await service.GetAsync(s.GroupId, "2026-11-10-2000", s.Boris, false, Now));
        // Опубликованный, но глубже 90 дней — нет никому.
        Assert.Null(await service.GetAsync(s.GroupId, "2026-07-28-2000", s.Anna, false, Now));
        Assert.Null(await service.GetAsync(s.GroupId, "2026-07-28-2000", s.Coach, true, Now));

        // Тренер видит черновик как раньше — и его лишь ему.
        var coach = await service.GetAsync(s.GroupId, "2026-11-03-2000", s.Coach, true, Now);
        Assert.NotNull(coach);
        Assert.NotNull(coach!.People);
    }

    [Fact]
    public async Task MemberPast_WithinWeek_DraftPlanStaysHidden()
    {
        await using var db = CreateDb(nameof(MemberPast_WithinWeek_DraftPlanStaysHidden));
        var s = await SeedAsync(db);
        var service = new TrainingRsvpService(db);

        // 03.11 глазами участника через два дня после занятия: в окне недели оно открывается (как
        // раньше), но черновика в нём нет — вида по дорожкам нет, раскладки на лету для истории тоже.
        var dto = await service.GetAsync(s.GroupId, "2026-11-03-2000", s.Anna, false, new DateTime(2026, 11, 5, 12, 0, 0));
        Assert.NotNull(dto);
        Assert.Null(dto!.LaneView);
    }

    [Fact]
    public async Task UpcomingForMember_WorkoutStillOnlyOnOwnLane()
    {
        await using var db = CreateDb(nameof(UpcomingForMember_WorkoutStillOnlyOnOwnLane));
        var s = await SeedAsync(db);
        var service = new TrainingRsvpService(db);

        // Тот же план, но смотрим ДО начала: Boris идёт — workout только на его дорожке.
        var before = new DateTime(2026, 10, 27, 12, 0, 0);
        var dto = await service.GetAsync(s.GroupId, PublishedKey, s.Boris, false, before);

        Assert.Null(dto!.LaneView!.Lanes[0].LevelWorkout);
        Assert.NotNull(dto.LaneView.Lanes[1].LevelWorkout);
        // А Anna без ответа до начала на дорожке не стоит.
        Assert.Empty(dto.LaneView.Lanes[0].People);
    }

    // ── Ручка ───────────────────────────────────────────────────────────────

    private static HubGroupTrainingRsvpController Controller(SwimmDbContext db, int? userId, bool canEdit, bool member)
    {
        var permissions = new Mock<IHubGroupPermissionService>();
        permissions.Setup(p => p.GetPermissionsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new HubGroupPermissions(true, false, false, false, canEdit, false, false, false));
        var trainings = new Mock<IHubGroupTrainingRepository>();
        trainings.Setup(t => t.IsActiveAccountMemberAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(member);

        var claims = userId == null ? [] : new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) };
        return new HubGroupTrainingRsvpController(new TrainingRsvpService(db), permissions.Object, trainings.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
            },
        };
    }

    [Fact]
    public async Task PastTrainings_Member200WithoutCounts_Outsider403_Guest401()
    {
        await using var db = CreateDb(nameof(PastTrainings_Member200WithoutCounts_Outsider403_Guest401));
        var s = await SeedAsync(db);

        // Ручка берёт «сейчас» сама: занятие 27.10 уже прошло, окно — живое (тест после 25.01.2027
        // увидит пустой список, и это верно — проверяем только форму ответа и отказ).
        var ok = Assert.IsType<OkObjectResult>(await Controller(db, s.Anna, canEdit: false, member: true).PastTrainings(s.GroupId));
        Assert.All(Assert.IsType<List<PastTrainingDto>>(ok.Value), p => Assert.Null(p.Yes));

        Assert.IsType<ForbidResult>(await Controller(db, s.Outsider, canEdit: false, member: false).PastTrainings(s.GroupId));
        Assert.IsType<UnauthorizedResult>(await Controller(db, null, canEdit: false, member: false).PastTrainings(s.GroupId));
        Assert.IsType<ForbidResult>(await Controller(db, s.Outsider, canEdit: false, member: false).Get(s.GroupId, PublishedKey));
    }
}
