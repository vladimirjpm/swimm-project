using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Поток тренера CF4 (docs/plans/trainings-coach-flow-plan.md §5.3, §5.7): ответ ставит на дорожку и
/// снимает с неё, Undo, ответ за любого пловца состава без аккаунта (решение 14), «Needs you» и
/// перенос ответов при привязке аккаунта (решение 15).
/// </summary>
public class CoachFlowRsvpTests
{
    /// <summary>План занятия даты: CF5 — план у занятия (слот 20:00 в тестовых расписаниях).</summary>
    private static LanePlanKey At(DateOnly date, string start = "20:00") => LanePlanKey.Session(date, start);

    private static readonly DateTime MondayNoon = new(2026, 9, 28, 12, 0, 0);
    private static readonly DateOnly Tue = new(2026, 9, 29);
    private const string TueKey = "2026-09-29-2000";

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>
    /// Вторник 20:00. Anna — аккаунт с меткой на пловчиху Anna (уровень Fast); Vera — аккаунт без
    /// пловца; Gil — пловец федерации без аккаунта (НЕ заведён в группе), уровень Fast; Dan — пловец
    /// без уровня и без аккаунта. План вторника: 2 дорожки Fast, все в Unassigned.
    /// </summary>
    private sealed record Seed(int GroupId, int Coach, int Anna, int Vera, int AnnaSwimmer, int Gil, int Dan, int Fast);

    private static async Task<Seed> SeedAsync(SwimmDbContext db, bool withPlan = true)
    {
        AppUser U(string name) => new() { Email = $"{name}@example.com", DisplayName = name, SecurityStamp = "s" };
        var coach = U("Coach");
        var anna = U("Anna");
        var vera = U("Vera");
        var annaSwimmer = new Swimmer { LastName = "A", FirstName = "Anna", BirthYear = 1980 };
        var gil = new Swimmer { LastName = "G", FirstName = "Gil", BirthYear = 1975 };
        var dan = new Swimmer { LastName = "D", FirstName = "Dan", BirthYear = 1976 };
        var schedule = new GroupTrainingSchedule { Slots = [new GroupTrainingSlot { Day = 2, Start = "20:00", End = "21:00" }] };
        var group = new HubGroup { Name = "Dolphins", Slug = "dolphins", Owner = coach, TrainingSchedule = schedule.ToJson() };
        db.AddRange(coach, anna, vera, annaSwimmer, gil, dan, group);
        await db.SaveChangesAsync();

        var fast = new HubGroupLevel { HubGroupId = group.Id, Rank = 1, Name = "Fast" };
        db.HubGroupLevels.Add(fast);
        db.HubGroupUserMembers.AddRange(
            new HubGroupUserMember { HubGroupId = group.Id, UserId = anna.Id, SwimmerId = annaSwimmer.Id },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = vera.Id });
        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = annaSwimmer.Id, SortOrder = 1 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = gil.Id, SortOrder = 2 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = dan.Id, SortOrder = 3 });
        await db.SaveChangesAsync();
        db.HubGroupSwimmerLevels.AddRange(
            new HubGroupSwimmerLevel { HubGroupId = group.Id, SwimmerId = annaSwimmer.Id, LevelId = fast.Id },
            new HubGroupSwimmerLevel { HubGroupId = group.Id, SwimmerId = gil.Id, LevelId = fast.Id });
        await db.SaveChangesAsync();

        if (withPlan)
        {
            var saved = await new LanePlanService(db).SaveAsync(group.Id, At(Tue), new LanePlanInputDto
            {
                LaneCount = 2,
                Lanes = [new LanePlanLaneInputDto { LaneNo = 1, LevelId = fast.Id }, new LanePlanLaneInputDto { LaneNo = 2, LevelId = fast.Id }],
                Swimmers = [
                    new LanePlanSwimmerInputDto { SwimmerId = annaSwimmer.Id },
                    new LanePlanSwimmerInputDto { SwimmerId = gil.Id },
                    new LanePlanSwimmerInputDto { SwimmerId = dan.Id },
                ],
            }, coach.Id);
            Assert.True(saved.Success, saved.Error);
        }
        return new Seed(group.Id, coach.Id, anna.Id, vera.Id, annaSwimmer.Id, gil.Id, dan.Id, fast.Id);
    }

    private static Task<TrainingRsvpSaveResult> AnswerAsync(SwimmDbContext db, Seed s, TrainingRsvpInputDto input, int? actor = null) =>
        new TrainingRsvpService(db).SetAsync(s.GroupId, TueKey, actor ?? s.Coach, actor == null, input, MondayNoon);

    private static async Task<(int? LaneNo, bool InPlan)> PlaceAsync(SwimmDbContext db, int swimmerId)
    {
        var row = await db.LanePlanSwimmers.AsNoTracking().FirstOrDefaultAsync(r => r.SwimmerId == swimmerId);
        return (row?.LaneNo, row != null);
    }

    [Fact]
    public async Task Going_PlacesOnOwnLevelLane_Out_RemovesToNotToday_UndoRestores()
    {
        await using var db = CreateDb(nameof(Going_PlacesOnOwnLevelLane_Out_RemovesToNotToday_UndoRestores));
        var s = await SeedAsync(db);

        // Участница сама: «Going» ставит её пловчиху на дорожку 1 (обе пустые — меньший номер).
        var self = (await AnswerAsync(db, s, new TrainingRsvpInputDto { Answer = "yes" }, actor: s.Anna)).Rsvp!;
        Assert.Equal((1, true), await PlaceAsync(db, s.AnnaSwimmer));
        Assert.Equal(1, self.LaneChange!.After.LaneNo);

        // Тренер за Gil — на свободную дорожку 2.
        var gil = (await AnswerAsync(db, s, new TrainingRsvpInputDto { Answer = "yes", SwimmerId = s.Gil })).Rsvp!;
        Assert.Equal(new LaneChangeDto
        {
            SwimmerId = s.Gil,
            Before = new LanePlaceDto { InPlan = true, LaneNo = null, OrderNo = 0 },
            After = new LanePlaceDto { InPlan = true, LaneNo = 2, OrderNo = 0 },
        }.After.LaneNo, gil.LaneChange!.After.LaneNo);
        Assert.Null(gil.LaneChange.Before.LaneNo);

        // Undo: ответ снят, Gil — обратно в Unassigned.
        var undo = (await AnswerAsync(db, s, new TrainingRsvpInputDto { Answer = null, SwimmerId = s.Gil, RestoreLane = gil.LaneChange.Before })).Rsvp!;
        Assert.Equal((null, true), await PlaceAsync(db, s.Gil));
        Assert.Null(undo.LaneChange!.After.LaneNo);

        // «Can't come» — в «Not today» (строки в плане нет).
        await AnswerAsync(db, s, new TrainingRsvpInputDto { Answer = "no", SwimmerId = s.Gil });
        Assert.Equal((null, false), await PlaceAsync(db, s.Gil));

        // Повторный «Going» уже стоящего на дорожке не двигает.
        var again = (await AnswerAsync(db, s, new TrainingRsvpInputDto { Answer = "yes" }, actor: s.Anna)).Rsvp!;
        Assert.Null(again.LaneChange);
    }

    [Fact]
    public async Task Going_WithoutLevel_Unassigned_And_InNeedsYou_PlaceAfterLevel()
    {
        await using var db = CreateDb(nameof(Going_WithoutLevel_Unassigned_And_InNeedsYou_PlaceAfterLevel));
        var s = await SeedAsync(db);

        var dto = (await AnswerAsync(db, s, new TrainingRsvpInputDto { Answer = "yes", SwimmerId = s.Dan })).Rsvp!;
        Assert.Null(dto.LaneChange);                                       // был в Unassigned — там и остался
        Assert.Equal([s.Dan], dto.Needs!.NoLevel.Select(n => n.SwimmerId));
        Assert.Equal([s.Vera], dto.Needs.Unlinked.Select(n => n.UserId));   // Vera ни с кем не связана

        db.HubGroupSwimmerLevels.Add(new HubGroupSwimmerLevel { HubGroupId = s.GroupId, SwimmerId = s.Dan, LevelId = s.Fast });
        await db.SaveChangesAsync();
        var placed = await new LanePlanService(db).PlaceAsync(s.GroupId, At(Tue), s.Dan, s.Coach);
        Assert.Equal(1, placed!.After.LaneNo);

        var after = (await new TrainingRsvpService(db).GetAsync(s.GroupId, TueKey, s.Coach, true, MondayNoon))!;
        Assert.Empty(after.Needs!.NoLevel);
        var dan = after.People!.Single(p => p.SwimmerId == s.Dan);
        Assert.Equal((s.Dan, s.Fast), (dan.LinkedSwimmerId, dan.LevelId));
    }

    [Fact]
    public async Task NoPlan_NoLaneChange()
    {
        await using var db = CreateDb(nameof(NoPlan_NoLaneChange));
        var s = await SeedAsync(db, withPlan: false);
        var dto = (await AnswerAsync(db, s, new TrainingRsvpInputDto { Answer = "yes", SwimmerId = s.Gil })).Rsvp!;
        Assert.Null(dto.LaneChange);
        Assert.Equal(1, dto.Yes);
    }

    [Fact]
    public async Task FederationSwimmerWithoutAccount_InRollCallAndDenominator()
    {
        await using var db = CreateDb(nameof(FederationSwimmerWithoutAccount_InRollCallAndDenominator));
        var s = await SeedAsync(db);
        var dto = (await new TrainingRsvpService(db).GetAsync(s.GroupId, TueKey, s.Coach, true, MondayNoon))!;
        // Anna, Vera + Gil и Dan (из справочника, без аккаунта); пловчиха Anna — аккаунтом, не дважды.
        Assert.Equal(4, dto.Total);
        Assert.Equal(new int?[] { s.Dan, s.Gil }, dto.People!.Where(p => p.UserId == null).Select(p => p.SwimmerId).OrderBy(x => x == s.Gil));
        var anna = dto.People!.Single(p => p.UserId == s.Anna);
        Assert.Equal((s.AnnaSwimmer, "A Anna"), (anna.LinkedSwimmerId, anna.LinkedSwimmerName));

        // За пловчиху, которой стоит аккаунт, — нельзя.
        var blocked = await AnswerAsync(db, s, new TrainingRsvpInputDto { Answer = "yes", SwimmerId = s.AnnaSwimmer });
        Assert.Equal(400, blocked.Status);
    }

    [Fact]
    public async Task Linking_MovesFutureCoachAnswers_AccountOwnAnswerWins()
    {
        await using var db = CreateDb(nameof(Linking_MovesFutureCoachAnswers_AccountOwnAnswerWins));
        var s = await SeedAsync(db);
        var users = new HubGroupUserService(db, new HubGroupCrudCore(db), new SettingsStub());
        var future = DateOnly.FromDateTime(IsraelTime.ToLocal(DateTime.UtcNow)).AddDays(3);
        var coachRows = new[]
        {
            new HubGroupTrainingRsvp { HubGroupId = s.GroupId, SessionDate = future, SessionStart = "20:00", SwimmerId = s.Gil, Answer = "yes", SetByUserId = s.Coach },
            new HubGroupTrainingRsvp { HubGroupId = s.GroupId, SessionDate = future, SessionStart = "07:00", SwimmerId = s.Gil, Answer = "yes", SetByUserId = s.Coach },
        };
        db.HubGroupTrainingRsvps.AddRange(coachRows);
        db.HubGroupTrainingRsvps.Add(new HubGroupTrainingRsvp { HubGroupId = s.GroupId, SessionDate = future, SessionStart = "07:00", UserId = s.Vera, Answer = "no" });
        await db.SaveChangesAsync();

        var result = await users.SetUserMemberLabelAsync(s.GroupId, s.Vera, s.Gil, null);
        Assert.True(result.Success, result.Error);

        var rows = await db.HubGroupTrainingRsvps.AsNoTracking().Where(r => r.SessionDate == future).OrderBy(r => r.SessionStart).ToListAsync();
        Assert.All(rows, r => Assert.Equal((s.Vera, (int?)null), (r.UserId!.Value, r.SwimmerId)));
        Assert.Equal(["no", "yes"], rows.Select(r => r.Answer));           // 07:00 — её ответ; 20:00 — от тренера
        Assert.Equal(s.Coach, rows[1].SetByUserId);
    }

    [Fact]
    public async Task MeWithoutCoachLabel_AccountGetsCoachAnswer_OwnAnswerReplacesIt()
    {
        await using var db = CreateDb(nameof(MeWithoutCoachLabel_AccountGetsCoachAnswer_OwnAnswerReplacesIt));
        var s = await SeedAsync(db);
        Assert.NotNull((await AnswerAsync(db, s, new TrainingRsvpInputDto { Answer = "yes", SwimmerId = s.Gil })).Rsvp);

        // Vera завела аккаунт и отметила Gil «Me» — метку тренер не ставил, ответы никто не переносил.
        db.UserFavorites.Add(new UserFavorite { UserId = s.Vera, TargetType = "swimmer", SwimmerId = s.Gil, IsPrimary = true });
        await db.SaveChangesAsync();

        var service = new TrainingRsvpService(db);
        var mine = (await service.GetAsync(s.GroupId, TueKey, s.Vera, false, MondayNoon))!;
        Assert.Equal(("yes", true), (mine.Mine?.Answer, mine.Mine!.SetByCoach));       // не «не ответила»

        var coach = (await service.GetAsync(s.GroupId, TueKey, s.Coach, true, MondayNoon))!;
        Assert.Equal("yes", coach.People!.Single(p => p.UserId == s.Vera).Answer);
        Assert.DoesNotContain(coach.People!, p => p.SwimmerId == s.Gil);              // человек один
        Assert.Equal(1, coach.Yes);

        // Сама ответила — ответ тренера за пловца удалён, двух ответов у Gil нет.
        var own = (await AnswerAsync(db, s, new TrainingRsvpInputDto { Answer = "no" }, actor: s.Vera)).Rsvp!;
        Assert.Equal((0, 1), (own.Yes, own.No));
        Assert.False(await db.HubGroupTrainingRsvps.AnyAsync(r => r.SwimmerId == s.Gil));
    }

    [Fact]
    public async Task NotSwimmer_LeavesNeedsYou_LinkingClearsTheFlag()
    {
        await using var db = CreateDb(nameof(NotSwimmer_LeavesNeedsYou_LinkingClearsTheFlag));
        var s = await SeedAsync(db);
        var users = new HubGroupUserService(db, new HubGroupCrudCore(db), new SettingsStub());
        await users.SetUserMemberLabelAsync(s.GroupId, s.Vera, s.Gil, "parent");

        var result = await users.SetUserMemberNotSwimmerAsync(s.GroupId, s.Vera);
        Assert.True(result.Success, result.Error);
        var vera = await db.HubGroupUserMembers.AsNoTracking().SingleAsync(m => m.HubGroupId == s.GroupId && m.UserId == s.Vera);
        Assert.Equal((true, (int?)null, (string?)null), (vera.NotSwimmer, vera.SwimmerId, vera.Note));   // метка снята

        var dto = (await new TrainingRsvpService(db).GetAsync(s.GroupId, TueKey, s.Coach, true, MondayNoon))!;
        Assert.Empty(dto.Needs!.Unlinked);                                  // больше не зовём тренера

        await users.SetUserMemberLabelAsync(s.GroupId, s.Vera, s.Gil, null);
        Assert.False((await db.HubGroupUserMembers.AsNoTracking().SingleAsync(m => m.HubGroupId == s.GroupId && m.UserId == s.Vera)).NotSwimmer);

        Assert.False((await users.SetUserMemberNotSwimmerAsync(s.GroupId, 999)).Success);
    }

    private sealed class SettingsStub : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }
}
