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
/// План дорожек принадлежит ЗАНЯТИЮ, а не дате (docs/plans/trainings-coach-flow-plan.md, CF5,
/// решение 1): ключ <c>yyyy-MM-dd-HHmm</c>, у двух занятий одного дня — разные планы; плана на день
/// без тренировки нет; старый план дня (Start null) — только для чтения; ответ на одно занятие
/// двигает человека только в плане этого занятия.
/// </summary>
public class LanePlanSessionKeyTests
{
    /// <summary>Вторник: занятия 07:00 и 20:00 (слоты расписания).</summary>
    private static readonly DateOnly Tue = new(2026, 9, 29);

    private static readonly DateTime MondayNoon = new(2026, 9, 28, 12, 0, 0);

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    // ── LanePlanKey ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-09-29-2000", true, "20:00")]
    [InlineData("2026-09-29-0700", true, "07:00")]
    [InlineData("2026-09-29", true, null)]
    [InlineData("2026-09-29-2460", false, null)]
    [InlineData("1999-12-31-2000", false, null)]
    [InlineData("2026-9-29", false, null)]
    [InlineData("", false, null)]
    public void Key_ParsesSessionOrBareDate(string raw, bool ok, string? start)
    {
        Assert.Equal(ok, LanePlanKey.TryParse(raw, out var key));
        if (!ok) return;
        Assert.Equal(Tue, key.Date);
        Assert.Equal(start, key.Start);
        Assert.Equal(start != null, key.IsSession);
        Assert.Equal(raw, key.ToString());
    }

    // ── Сервис плана ────────────────────────────────────────────────────────

    private sealed record Seed(int GroupId, int Coach, int Anna, int AnnaSwimmer, int Gil, int Fast);

    /// <summary>
    /// Группа с занятиями вторника 07:00 и 20:00. Anna — участница-аккаунт, стоит пловчихой Anna (Fast);
    /// Gil — пловец состава без аккаунта (Fast).
    /// </summary>
    private static async Task<Seed> SeedAsync(SwimmDbContext db)
    {
        AppUser U(string name) => new() { Email = $"{name}@example.com", DisplayName = name, SecurityStamp = "s" };
        var coach = U("Coach");
        var anna = U("Anna");
        var annaSwimmer = new Swimmer { LastName = "A", FirstName = "Anna", BirthYear = 1980 };
        var gil = new Swimmer { LastName = "G", FirstName = "Gil", BirthYear = 1975 };
        var schedule = new GroupTrainingSchedule
        {
            Slots =
            [
                new GroupTrainingSlot { Day = 2, Start = "07:00", End = "08:00" },
                new GroupTrainingSlot { Day = 2, Start = "20:00", End = "21:00" },
            ],
        };
        var group = new HubGroup { Name = "Dolphins", Slug = "dolphins", Owner = coach, TrainingSchedule = schedule.ToJson() };
        db.AddRange(coach, anna, annaSwimmer, gil, group);
        await db.SaveChangesAsync();

        var fast = new HubGroupLevel { HubGroupId = group.Id, Rank = 1, Name = "Fast" };
        db.HubGroupLevels.Add(fast);
        db.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = group.Id, UserId = anna.Id, SwimmerId = annaSwimmer.Id });
        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = annaSwimmer.Id, SortOrder = 1 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = gil.Id, SortOrder = 2 });
        await db.SaveChangesAsync();
        db.HubGroupSwimmerLevels.AddRange(
            new HubGroupSwimmerLevel { HubGroupId = group.Id, SwimmerId = annaSwimmer.Id, LevelId = fast.Id },
            new HubGroupSwimmerLevel { HubGroupId = group.Id, SwimmerId = gil.Id, LevelId = fast.Id });
        await db.SaveChangesAsync();
        return new Seed(group.Id, coach.Id, anna.Id, annaSwimmer.Id, gil.Id, fast.Id);
    }

    private static LanePlanInputDto Plan(Seed s, int laneCount, params (int SwimmerId, int? LaneNo)[] swimmers) => new()
    {
        LaneCount = laneCount,
        Lanes = Enumerable.Range(1, laneCount).Select(n => new LanePlanLaneInputDto { LaneNo = n, LevelId = s.Fast }).ToList(),
        Swimmers = swimmers.Select(w => new LanePlanSwimmerInputDto { SwimmerId = w.SwimmerId, LaneNo = w.LaneNo }).ToList(),
    };

    [Fact]
    public async Task TwoTrainingsOfOneDay_HaveTheirOwnPlans()
    {
        await using var db = CreateDb(nameof(TwoTrainingsOfOneDay_HaveTheirOwnPlans));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);
        var morning = LanePlanKey.Session(Tue, "07:00");
        var evening = LanePlanKey.Session(Tue, "20:00");

        Assert.True((await service.SaveAsync(s.GroupId, morning, Plan(s, 2, (s.AnnaSwimmer, 1)), s.Coach)).Success);
        Assert.True((await service.SaveAsync(s.GroupId, evening, Plan(s, 4, (s.Gil, 3)), s.Coach)).Success);
        Assert.True(await service.SetStatusAsync(s.GroupId, evening, LanePlanStatus.Published));

        var am = (await service.GetAsync(s.GroupId, morning, isManager: true))!;
        var pm = (await service.GetAsync(s.GroupId, evening, isManager: true))!;
        Assert.Equal(("2026-09-29-0700", "07:00", 2, LanePlanStatus.Draft), (am.SessionId, am.Start, am.LaneCount, am.Status));
        Assert.Equal(("2026-09-29-2000", "20:00", 4, LanePlanStatus.Published), (pm.SessionId, pm.Start, pm.LaneCount, pm.Status));
        Assert.Equal([s.AnnaSwimmer], am.Lanes[0].Swimmers.Select(w => w.SwimmerId));
        Assert.Equal([s.Gil], pm.Lanes[2].Swimmers.Select(w => w.SwimmerId));

        // Список — оба плана дня, новые (позднее начало) сверху, с ключом занятия.
        Assert.Equal(["2026-09-29-2000", "2026-09-29-0700"], (await service.ListAsync(s.GroupId, isManager: true)).Select(p => p.SessionId));
        // Удаление одного не трогает другой.
        Assert.True(await service.DeleteAsync(s.GroupId, morning));
        Assert.NotNull(await service.GetAsync(s.GroupId, evening, isManager: true));
        Assert.Null(await service.GetAsync(s.GroupId, morning, isManager: true));
    }

    [Fact]
    public async Task NoTraining_NoNewPlan_ExistingPlanStillSaves()
    {
        await using var db = CreateDb(nameof(NoTraining_NoNewPlan_ExistingPlanStillSaves));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);

        // Вторник 12:00 — такого занятия нет; среда — занятий нет вовсе.
        var noon = await service.SaveAsync(s.GroupId, LanePlanKey.Session(Tue, "12:00"), Plan(s, 2), s.Coach);
        var wednesday = await service.SaveAsync(s.GroupId, LanePlanKey.Session(Tue.AddDays(1), "20:00"), Plan(s, 2), s.Coach);
        Assert.False(noon.Success);
        Assert.False(wednesday.Success);
        Assert.False(await db.LanePlans.AnyAsync());

        // Разовое занятие («Ask who's coming» с началом) — тоже занятие: план к нему заводится.
        db.TrainingSessions.Add(new TrainingSession
        {
            HubGroupId = s.GroupId, Date = DateTime.SpecifyKind(Tue.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            StartTime = "18:30", AsksRsvp = true, Title = "Open water", CreatedByUserId = s.Coach,
        });
        await db.SaveChangesAsync();
        Assert.True((await service.SaveAsync(s.GroupId, LanePlanKey.Session(Tue.AddDays(1), "18:30"), Plan(s, 2), s.Coach)).Success);

        // Занятие убрали из расписания — уже заведённый план по-прежнему сохраняется (не теряем работу).
        Assert.True((await service.SaveAsync(s.GroupId, LanePlanKey.Session(Tue, "20:00"), Plan(s, 2), s.Coach)).Success);
        (await db.HubGroups.SingleAsync()).TrainingSchedule = new GroupTrainingSchedule().ToJson();
        await db.SaveChangesAsync();
        Assert.True((await service.SaveAsync(s.GroupId, LanePlanKey.Session(Tue, "20:00"), Plan(s, 3), s.Coach)).Success);
    }

    [Fact]
    public async Task DayPlanWithoutTraining_IsReadOnly_AndBareDateFindsTheDay()
    {
        await using var db = CreateDb(nameof(DayPlanWithoutTraining_IsReadOnly_AndBareDateFindsTheDay));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);
        var thursday = Tue.AddDays(2);
        // План дня, которому миграция не нашла занятия (Start null), — опубликован.
        db.LanePlans.Add(new LanePlan
        {
            HubGroupId = s.GroupId, Date = thursday, LaneCount = 3, Status = LanePlanStatus.Published, CreatedByUserId = s.Coach,
        });
        await db.SaveChangesAsync();
        var day = new LanePlanKey(thursday, null);

        var board = (await service.GetAsync(s.GroupId, day, isManager: true))!;
        Assert.Null(board.SessionId);
        Assert.Null(board.Start);
        Assert.False(board.CanEdit);
        Assert.NotNull(await service.GetAsync(s.GroupId, day, isManager: false));
        Assert.Null(Assert.Single(await service.ListAsync(s.GroupId, isManager: true)).SessionId);

        // Писать по дате нельзя ничего.
        Assert.False((await service.SaveAsync(s.GroupId, day, Plan(s, 2), s.Coach)).Success);
        Assert.False(await service.SetStatusAsync(s.GroupId, day, LanePlanStatus.Draft));
        Assert.False((await service.PublishAsync(s.GroupId, day)).Found);
        Assert.False((await service.SaveWorkoutAsync(s.GroupId, day, null)).Found);
        Assert.Null(await service.GetPublishCheckAsync(s.GroupId, day));
        Assert.False(await service.DeleteAsync(s.GroupId, day));
        Assert.Equal(LanePlanStatus.Published, (await db.LanePlans.SingleAsync()).Status);

        // Старая ссылка по дате на день с планами занятий — план первого по времени занятия.
        await service.SaveAsync(s.GroupId, LanePlanKey.Session(Tue, "20:00"), Plan(s, 4), s.Coach);
        await service.SaveAsync(s.GroupId, LanePlanKey.Session(Tue, "07:00"), Plan(s, 2), s.Coach);
        Assert.Equal("2026-09-29-0700", (await service.GetAsync(s.GroupId, new LanePlanKey(Tue, null), isManager: true))!.SessionId);
        // Участнику — первый опубликованный: утренний черновик не прячет вечерний план.
        Assert.Null(await service.GetAsync(s.GroupId, new LanePlanKey(Tue, null), isManager: false));
        await service.SetStatusAsync(s.GroupId, LanePlanKey.Session(Tue, "20:00"), LanePlanStatus.Published);
        Assert.Equal("2026-09-29-2000", (await service.GetAsync(s.GroupId, new LanePlanKey(Tue, null), isManager: false))!.SessionId);
    }

    [Fact]
    public async Task Answer_MovesOnlyInThePlanOfItsTraining()
    {
        await using var db = CreateDb(nameof(Answer_MovesOnlyInThePlanOfItsTraining));
        var s = await SeedAsync(db);
        var plans = new LanePlanService(db);
        var morning = LanePlanKey.Session(Tue, "07:00");
        var evening = LanePlanKey.Session(Tue, "20:00");
        // Anna стоит на дорожке 1 в обоих планах; Gil — нигде (Not today).
        await plans.SaveAsync(s.GroupId, morning, Plan(s, 2, (s.AnnaSwimmer, 1)), s.Coach);
        await plans.SaveAsync(s.GroupId, evening, Plan(s, 2, (s.AnnaSwimmer, 1)), s.Coach);
        var rsvp = new TrainingRsvpService(db);

        // «Can't come» на утро снимает Anna только с утреннего плана.
        var no = await rsvp.SetAsync(s.GroupId, morning.ToString(), s.Anna, false, new TrainingRsvpInputDto { Answer = "no" }, MondayNoon);
        Assert.Null(no.Error);
        Assert.Empty((await plans.GetAsync(s.GroupId, morning, isManager: true))!.Lanes.SelectMany(l => l.Swimmers));
        Assert.Equal([s.AnnaSwimmer], (await plans.GetAsync(s.GroupId, evening, isManager: true))!.Lanes[0].Swimmers.Select(w => w.SwimmerId));

        // «Going» за Gil на вечер ставит его только в вечерний план.
        var yes = await rsvp.SetAsync(s.GroupId, evening.ToString(), s.Coach, true,
            new TrainingRsvpInputDto { Answer = "yes", SwimmerId = s.Gil }, MondayNoon);
        Assert.Null(yes.Error);
        Assert.Equal(2, yes.Rsvp!.LaneChange!.After.LaneNo);
        Assert.Contains(s.Gil, (await plans.GetAsync(s.GroupId, evening, isManager: true))!.Lanes.SelectMany(l => l.Swimmers).Select(w => w.SwimmerId));
        Assert.DoesNotContain(s.Gil, (await plans.GetAsync(s.GroupId, morning, isManager: true))!.Lanes.SelectMany(l => l.Swimmers).Select(w => w.SwimmerId));
    }
}
