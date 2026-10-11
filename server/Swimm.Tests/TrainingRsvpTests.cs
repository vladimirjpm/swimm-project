using Microsoft.EntityFrameworkCore;
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
/// Ответы «иду / не уверен / не приду» на занятие группы (docs/plans/entity-hero-roles-plan.md,
/// Ш2): ключ и окно занятия (<see cref="TrainingRsvpRules"/>) и сервис
/// (<see cref="TrainingRsvpService"/>). Решения Влада 28.09.2026: отвечает ПОЛЬЗОВАТЕЛЬ, тренер
/// может ответить за участника; полоса считается по активным участникам-аккаунтам.
/// </summary>
public class TrainingRsvpTests
{
    // Вторник 29.09.2026 — ближайшее занятие; «сейчас» — понедельник 28.09, полдень.
    private static readonly DateOnly Tue = new(2026, 9, 29);
    private static readonly DateTime MondayNoon = new(2026, 9, 28, 12, 0, 0);
    private const string TueKey = "2026-09-29-2000";

    private static GroupTrainingSchedule TueSat() => new()
    {
        Slots =
        [
            new GroupTrainingSlot { Day = 2, Start = "20:00", End = "21:00" },
            new GroupTrainingSlot { Day = 6, Start = "10:00", End = "12:00" },
        ],
    };

    /// <summary>Каждый день в 20:00 — для проверок окна без подбора дня недели.</summary>
    private static GroupTrainingSchedule Daily() => new()
    {
        Slots = Enumerable.Range(1, 7).Select(d => new GroupTrainingSlot { Day = d, Start = "20:00" }).ToList(),
    };

    // ── TrainingRsvpRules ───────────────────────────────────────────────────

    [Fact]
    public void SessionKey_RoundTrips()
    {
        var key = TrainingRsvpRules.SessionKey(Tue, "20:00");

        Assert.Equal(TueKey, key);
        Assert.True(TrainingRsvpRules.TryParseSessionKey(key, out var date, out var start));
        Assert.Equal((Tue, "20:00"), (date, start));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-09-29 2000")]
    [InlineData("2026-09-29-2500")]
    [InlineData("2026-13-01-2000")]
    [InlineData("2026-09-29-20:00")]
    public void SessionKey_Garbage_IsRejected(string? key)
    {
        Assert.False(TrainingRsvpRules.TryParseSessionKey(key, out _, out _));
    }

    [Fact]
    public void FindSlot_MatchesDayOfWeekAndStart()
    {
        var schedule = TueSat();

        Assert.NotNull(TrainingRsvpRules.FindSlot(schedule, Tue, "20:00"));
        Assert.Null(TrainingRsvpRules.FindSlot(schedule, Tue, "19:00"));               // не то время
        Assert.Null(TrainingRsvpRules.FindSlot(schedule, Tue.AddDays(1), "20:00"));    // среда
        Assert.NotNull(TrainingRsvpRules.FindSlot(schedule, new DateOnly(2026, 10, 3), "10:00")); // суббота
    }

    [Fact]
    public void Viewable_WindowIsWeekBackToFourWeeksAhead()
    {
        var today = DateOnly.FromDateTime(MondayNoon);
        var schedule = Daily();

        Assert.True(TrainingRsvpRules.IsViewable(schedule, today.AddDays(-TrainingRsvpRules.ManagerDaysBack), "20:00", MondayNoon));
        Assert.False(TrainingRsvpRules.IsViewable(schedule, today.AddDays(-TrainingRsvpRules.ManagerDaysBack - 1), "20:00", MondayNoon));
        Assert.True(TrainingRsvpRules.IsViewable(schedule, today.AddDays(TrainingRsvpRules.DaysAhead), "20:00", MondayNoon));
        Assert.False(TrainingRsvpRules.IsViewable(schedule, today.AddDays(TrainingRsvpRules.DaysAhead + 1), "20:00", MondayNoon));
        Assert.False(TrainingRsvpRules.IsViewable(schedule, today, "21:00", MondayNoon));  // нет в расписании
    }

    [Fact]
    public void Edit_MemberUntilStart_ManagerAWeekAfter()
    {
        var schedule = Daily();
        var today = DateOnly.FromDateTime(MondayNoon);
        var beforeStart = today.ToDateTime(new TimeOnly(19, 59));
        var atStart = today.ToDateTime(new TimeOnly(20, 0));

        Assert.Null(TrainingRsvpRules.EditBlockReason(schedule, today, "20:00", beforeStart, isManager: false));
        Assert.NotNull(TrainingRsvpRules.EditBlockReason(schedule, today, "20:00", atStart, isManager: false));
        // Тренер правит «кто пришёл» по факту — неделю после занятия.
        Assert.Null(TrainingRsvpRules.EditBlockReason(schedule, today, "20:00", atStart, isManager: true));
        Assert.Null(TrainingRsvpRules.EditBlockReason(schedule, today.AddDays(-7), "20:00", MondayNoon, isManager: true));
        Assert.NotNull(TrainingRsvpRules.EditBlockReason(schedule, today.AddDays(-8), "20:00", MondayNoon, isManager: true));
        // Вперёд — не дальше четырёх недель, никому.
        Assert.NotNull(TrainingRsvpRules.EditBlockReason(schedule, today.AddDays(29), "20:00", MondayNoon, isManager: true));
        Assert.NotNull(TrainingRsvpRules.EditBlockReason(schedule, today, "21:00", MondayNoon, isManager: true));
    }

    // ── TrainingRsvpService ─────────────────────────────────────────────────

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private sealed record Seed(int GroupId, int Coach, int Anna, int Boris, int Vera, int Pending, int Outsider);

    /// <summary>
    /// Группа с расписанием Tue/Sat; участники-аккаунты Anna (пловчиха), Boris (пловец), Vera
    /// (без пловца); Pending — заявка; Coach управляет группой, но в составе не состоит.
    /// </summary>
    private static async Task<Seed> SeedAsync(SwimmDbContext db)
    {
        AppUser U(string name) => new() { Email = $"{name}@example.com", DisplayName = name, SecurityStamp = "s" };
        var coach = U("Coach");
        var anna = U("Anna");
        var boris = U("Boris");
        var vera = U("Vera");
        var pending = U("Pending");
        var outsider = U("Outsider");
        var annaSwimmer = new Swimmer { LastName = "A", FirstName = "Anna", Gender = "female", BirthYear = 1980 };
        var borisSwimmer = new Swimmer { LastName = "B", FirstName = "Boris", Gender = "male", BirthYear = 1980 };
        var group = new HubGroup
        {
            Name = "Dolphins", Slug = "dolphins", Owner = coach, TrainingSchedule = TueSat().ToJson(),
        };
        db.AddRange(coach, anna, boris, vera, pending, outsider, annaSwimmer, borisSwimmer, group);
        await db.SaveChangesAsync();

        db.HubGroupUserMembers.AddRange(
            new HubGroupUserMember { HubGroupId = group.Id, UserId = anna.Id, SwimmerId = annaSwimmer.Id },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = boris.Id, SwimmerId = borisSwimmer.Id },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = vera.Id },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = pending.Id, Status = HubGroupUserMemberStatus.Pending });
        await db.SaveChangesAsync();
        return new Seed(group.Id, coach.Id, anna.Id, boris.Id, vera.Id, pending.Id, outsider.Id);
    }

    private static TrainingRsvpInputDto Answer(string? answer, string? note = null, int? userId = null) =>
        new() { Answer = answer, Note = note, UserId = userId };

    [Fact]
    public async Task Member_Answers_CountsAndMine_NoPeopleList()
    {
        await using var db = CreateDb(nameof(Member_Answers_CountsAndMine_NoPeopleList));
        var s = await SeedAsync(db);
        var service = new TrainingRsvpService(db);

        var result = await service.SetAsync(s.GroupId, TueKey, s.Anna, false, Answer("yes", "late"), MondayNoon);

        var dto = Assert.IsType<TrainingRsvpDto>(result.Rsvp);
        Assert.Equal((1, 0, 0, 3), (dto.Yes, dto.Maybe, dto.No, dto.Total));   // pending в знаменателе нет
        Assert.Equal(("yes", "late", false), (dto.Mine!.Answer, dto.Mine.Note, dto.Mine.SetByCoach));
        Assert.True(dto.IsMember);
        Assert.True(dto.CanAnswer);
        Assert.False(dto.CanManage);
        Assert.Null(dto.People);                         // список людей — только управляющему
        Assert.Equal(("2026-09-29", "20:00", "21:00"), (dto.Date, dto.Start, dto.End));
    }

    [Fact]
    public async Task SameButtonAgain_ClearsTheAnswer_AndNoteGoesWithNo()
    {
        await using var db = CreateDb(nameof(SameButtonAgain_ClearsTheAnswer_AndNoteGoesWithNo));
        var s = await SeedAsync(db);
        var service = new TrainingRsvpService(db);

        await service.SetAsync(s.GroupId, TueKey, s.Anna, false, Answer("no", "late"), MondayNoon);
        Assert.Null((await db.HubGroupTrainingRsvps.SingleAsync()).Note);    // к «не приду» заметка не нужна

        var cleared = await service.SetAsync(s.GroupId, TueKey, s.Anna, false, Answer(null), MondayNoon);

        Assert.Null(cleared.Rsvp!.Mine);
        Assert.Equal(0, cleared.Rsvp.No);
        Assert.Empty(db.HubGroupTrainingRsvps);
    }

    [Theory]
    [InlineData("going", null)]
    [InlineData("yes", "sleepy")]
    public async Task BadAnswerOrNote_Is400(string answer, string? note)
    {
        await using var db = CreateDb(nameof(BadAnswerOrNote_Is400) + answer + note);
        var s = await SeedAsync(db);

        var result = await new TrainingRsvpService(db).SetAsync(s.GroupId, TueKey, s.Anna, false, Answer(answer, note), MondayNoon);

        Assert.Equal(400, result.Status);
        Assert.Empty(db.HubGroupTrainingRsvps);
    }

    [Fact]
    public async Task NotInSchedule_OrStarted_IsRejected()
    {
        await using var db = CreateDb(nameof(NotInSchedule_OrStarted_IsRejected));
        var s = await SeedAsync(db);
        var service = new TrainingRsvpService(db);

        var wrongTime = await service.SetAsync(s.GroupId, "2026-09-29-1900", s.Anna, false, Answer("yes"), MondayNoon);
        var started = await service.SetAsync(s.GroupId, TueKey, s.Anna, false, Answer("yes"), Tue.ToDateTime(new TimeOnly(20, 5)));

        Assert.Equal(400, wrongTime.Status);
        Assert.Equal(400, started.Status);
        Assert.Null(await service.GetAsync(s.GroupId, "2026-09-29-1900", s.Anna, false, MondayNoon));
        Assert.Null(await service.GetAsync(s.GroupId, "garbage", s.Anna, false, MondayNoon));
    }

    [Fact]
    public async Task OnlyActiveMembersAnswerForThemselves()
    {
        await using var db = CreateDb(nameof(OnlyActiveMembersAnswerForThemselves));
        var s = await SeedAsync(db);
        var service = new TrainingRsvpService(db);

        var pending = await service.SetAsync(s.GroupId, TueKey, s.Pending, false, Answer("yes"), MondayNoon);
        // Управляющий вне состава в полосе не считается — сам за себя он не отвечает.
        var coachSelf = await service.SetAsync(s.GroupId, TueKey, s.Coach, true, Answer("yes"), MondayNoon);

        Assert.Equal(400, pending.Status);
        Assert.Equal(400, coachSelf.Status);
        Assert.Empty(db.HubGroupTrainingRsvps);
    }

    [Fact]
    public async Task Coach_AnswersForAMember_AndSeesEveryone()
    {
        await using var db = CreateDb(nameof(Coach_AnswersForAMember_AndSeesEveryone));
        var s = await SeedAsync(db);
        var service = new TrainingRsvpService(db);
        await service.SetAsync(s.GroupId, TueKey, s.Boris, false, Answer("maybe"), MondayNoon);

        var result = await service.SetAsync(s.GroupId, TueKey, s.Coach, true, Answer("yes", userId: s.Anna), MondayNoon);

        var dto = result.Rsvp!;
        Assert.True(dto.CanManage);
        Assert.False(dto.IsMember);
        Assert.Null(dto.Mine);
        // Порядок: иду → не уверен → без ответа; пол — по пловцу участника.
        Assert.Equal(
            [("Anna", "yes", "female", true), ("Boris", "maybe", "male", false), ("Vera", null, null, false)],
            dto.People!.Select(p => (p.Name, p.Answer, p.Gender, p.SetByCoach)));

        // Анна видит у себя, что ответ поставил тренер.
        var anna = await service.GetAsync(s.GroupId, TueKey, s.Anna, false, MondayNoon);
        Assert.True(anna!.Mine!.SetByCoach);

        // Анна переотвечает сама — пометка «тренер» снимается.
        var own = await service.SetAsync(s.GroupId, TueKey, s.Anna, false, Answer("no"), MondayNoon);
        Assert.False(own.Rsvp!.Mine!.SetByCoach);
    }

    [Fact]
    public async Task ForSomeoneElse_OnlyTheCoach_AndOnlyForActiveMembers()
    {
        await using var db = CreateDb(nameof(ForSomeoneElse_OnlyTheCoach_AndOnlyForActiveMembers));
        var s = await SeedAsync(db);
        var service = new TrainingRsvpService(db);

        var byMember = await service.SetAsync(s.GroupId, TueKey, s.Anna, false, Answer("yes", userId: s.Boris), MondayNoon);
        var forOutsider = await service.SetAsync(s.GroupId, TueKey, s.Coach, true, Answer("yes", userId: s.Outsider), MondayNoon);
        var forPending = await service.SetAsync(s.GroupId, TueKey, s.Coach, true, Answer("yes", userId: s.Pending), MondayNoon);

        Assert.Equal(403, byMember.Status);
        Assert.Equal(400, forOutsider.Status);
        Assert.Equal(400, forPending.Status);
        Assert.Empty(db.HubGroupTrainingRsvps);
    }

    [Fact]
    public async Task AnswersOfPeopleWhoLeft_AreNotCounted()
    {
        await using var db = CreateDb(nameof(AnswersOfPeopleWhoLeft_AreNotCounted));
        var s = await SeedAsync(db);
        // Строка осталась от того, кто теперь только в заявке (или ушёл).
        db.HubGroupTrainingRsvps.Add(new HubGroupTrainingRsvp
        {
            HubGroupId = s.GroupId, SessionDate = Tue, SessionStart = "20:00", UserId = s.Pending, Answer = "yes",
        });
        await db.SaveChangesAsync();

        var dto = await new TrainingRsvpService(db).GetAsync(s.GroupId, TueKey, s.Coach, true, MondayNoon);

        Assert.Equal((0, 3), (dto!.Yes, dto.Total));
        Assert.DoesNotContain(dto.People!, p => p.UserId == s.Pending);
    }

    [Fact]
    public async Task LeavingTheGroup_DropsUpcomingAnswers_KeepsPastOnes()
    {
        await using var db = CreateDb(nameof(LeavingTheGroup_DropsUpcomingAnswers_KeepsPastOnes));
        var s = await SeedAsync(db);
        var today = DateOnly.FromDateTime(IsraelTimeNow());
        db.HubGroupTrainingRsvps.AddRange(
            new HubGroupTrainingRsvp { HubGroupId = s.GroupId, SessionDate = today.AddDays(3), SessionStart = "20:00", UserId = s.Anna, Answer = "yes" },
            new HubGroupTrainingRsvp { HubGroupId = s.GroupId, SessionDate = today.AddDays(-3), SessionStart = "20:00", UserId = s.Anna, Answer = "yes" },
            new HubGroupTrainingRsvp { HubGroupId = s.GroupId, SessionDate = today.AddDays(3), SessionStart = "20:00", UserId = s.Boris, Answer = "yes" });
        await db.SaveChangesAsync();

        var result = await new HubGroupUserService(db, new HubGroupCrudCore(db), new NullSettings()).LeaveAsync(s.GroupId, s.Anna);

        Assert.True(result.Success);
        Assert.Equal(
            [(s.Anna, today.AddDays(-3)), (s.Boris, today.AddDays(3))],
            db.HubGroupTrainingRsvps.OrderBy(r => r.UserId).Select(r => new ValueTuple<int, DateOnly>(r.UserId, r.SessionDate)));
    }

    private static DateTime IsraelTimeNow() => Swimm.Application.Constants.IsraelTime.ToLocal(DateTime.UtcNow);

    private sealed class NullSettings : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }
}
