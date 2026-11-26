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
/// Workout занятия (docs/plans/trainings-coach-flow-plan.md, §5.5, CF6): форма документа
/// (<see cref="WorkoutRules"/>), версия уровня участнику, хранение при плане
/// (<see cref="LanePlanService.SaveWorkoutAsync"/>) и выдача в виде по дорожкам.
/// </summary>
public class WorkoutTests
{
    /// <summary>План занятия даты: CF5 — план у занятия (слот 20:00 в тестовых расписаниях).</summary>
    private static LanePlanKey At(DateOnly date, string start = "20:00") => LanePlanKey.Session(date, start);

    private const int Adv = 11, Int = 12;
    private static readonly DateOnly Day = new(2026, 11, 17);

    private static WorkoutLineDto Line(int reps, int distance, params (int Level, string SendOff)[] cells) => new()
    {
        Reps = reps,
        Distance = distance,
        Text = "חתירה",
        PerLevel = cells.ToDictionary(c => c.Level, c => new WorkoutCellDto { SendOff = c.SendOff }),
    };

    /// <summary>Сет для всех (времена обоим уровням) и сет «только Advanced».</summary>
    private static WorkoutDto Sample() => new()
    {
        Sets =
        [
            new WorkoutSetDto { Title = "סט 1", Lines = [Line(4, 100, (Adv, "1:40"), (Int, "1:50"))] },
            new WorkoutSetDto { Title = "רק רמה 4+5", LevelIds = [Adv], Lines = [Line(2, 100, (Adv, "1:50"))] },
        ],
    };

    // ── WorkoutRules ────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_TrimsAndDropsEmptyCells_EmptyDocumentIsNull()
    {
        var input = new WorkoutDto
        {
            Note = "  ",
            Sets =
            [
                new WorkoutSetDto
                {
                    Title = "  Set 1 ", Lines =
                    [
                        new WorkoutLineDto
                        {
                            Reps = 4, Distance = 0, Text = " free ",
                            PerLevel = new() { [Adv] = new() { SendOff = " 1:40 " }, [Int] = new() { SendOff = " " } },
                        },
                    ],
                },
            ],
        };

        var (w, error) = WorkoutRules.Normalize(input);

        Assert.Null(error);
        var line = Assert.Single(Assert.Single(w!.Sets).Lines);
        Assert.Equal(("Set 1", "free", (int?)null), (w.Sets[0].Title, line.Text, line.Distance));
        Assert.Equal("1:40", Assert.Single(line.PerLevel!).Value.SendOff);
        Assert.Null(w.Note);

        Assert.Equal((null, null), WorkoutRules.Normalize(new WorkoutDto { Note = " " }));
    }

    [Fact]
    public void Normalize_Rejects_OutOfRange()
    {
        var bad = new WorkoutDto { Sets = [new WorkoutSetDto { Repeat = 0 }] };
        Assert.NotNull(WorkoutRules.Normalize(bad).Error);

        bad = new WorkoutDto { Sets = [new WorkoutSetDto { Lines = [new WorkoutLineDto { Reps = 100 }] }] };
        Assert.NotNull(WorkoutRules.Normalize(bad).Error);

        bad = new WorkoutDto
        {
            Sets = [new WorkoutSetDto { Lines = [new WorkoutLineDto { PerLevel = new() { [Adv] = new() { SendOff = new string('1', 30) } } }] }],
        };
        Assert.NotNull(WorkoutRules.Normalize(bad).Error);
    }

    [Fact]
    public void SerializeParse_RoundTrip_KeepsHebrewAndLevelKeys()
    {
        var stored = WorkoutRules.Serialize(Sample());
        var back = WorkoutRules.Parse(stored)!;

        Assert.Equal("רק רמה 4+5", back.Sets[1].Title);
        Assert.Equal("1:50", back.Sets[0].Lines[0].PerLevel![Int].SendOff);
        Assert.Equal([Adv], back.Sets[1].LevelIds!);
        Assert.Null(WorkoutRules.Parse("{broken"));
    }

    [Fact]
    public void ForLevel_Intermediate_OwnTimesOnly_NoAdvancedOnlySet()
    {
        var mine = WorkoutRules.ForLevel(Sample(), Int)!;

        var set = Assert.Single(mine.Sets);
        Assert.Equal("סט 1", set.Title);
        var cell = Assert.Single(set.Lines[0].PerLevel!);
        Assert.Equal((Int, "1:50"), (cell.Key, cell.Value.SendOff));

        Assert.Equal(2, WorkoutRules.ForLevel(Sample(), Adv)!.Sets.Count);
        // Дорожка без уровня — только сеты для всех и без времён.
        var none = WorkoutRules.ForLevel(Sample(), null)!;
        Assert.Null(Assert.Single(none.Sets).Lines[0].PerLevel);
    }

    [Fact]
    public void ForLevel_UntitledSet_NumberedLikeCoachSheet()
    {
        WorkoutSetDto Plain(List<int>? levels) => new()
        {
            Repeat = 1, LevelIds = levels,
            Lines = [new WorkoutLineDto { Reps = 1, Distance = 100, Text = "x" }],
        };
        var workout = new WorkoutDto { Sets = [Plain(null), Plain([Adv]), Plain(null)] };

        Assert.Equal(["Set 1", "Set 3"], WorkoutRules.ForLevel(workout, Int)!.Sets.Select(s => s.Title));
        Assert.Equal(["Set 1", "Set 2", "Set 3"], WorkoutRules.ForLevel(workout, Adv)!.Sets.Select(s => s.Title));
    }

    // ── Метка «главный сет» (docs/plans/workout-print-plan.md, §3.1) ───────

    /// <summary>Два сета для всех (первый главный) и сет «только Advanced», тоже главный.</summary>
    private static WorkoutDto WithMain() => new()
    {
        Sets =
        [
            new WorkoutSetDto { Title = "מרכזי", Main = true, Lines = [Line(4, 100, (Adv, "1:40"), (Int, "1:50"))] },
            new WorkoutSetDto { Title = "חימום", Lines = [Line(2, 100, (Adv, "1:50"), (Int, "2:00"))] },
            new WorkoutSetDto { Title = "רק רמה 4+5", LevelIds = [Adv], Main = true, Lines = [Line(2, 100, (Adv, "1:50"))] },
        ],
    };

    [Fact]
    public void Normalize_KeepsMain_SeveralAllowed()
    {
        var (w, error) = WorkoutRules.Normalize(WithMain());

        Assert.Null(error);
        Assert.Equal([true, false, true], w!.Sets.Select(s => s.Main));
    }

    [Fact]
    public void ForLevel_CarriesMainToLevelVersion_LevelWithoutMainSetGetsNone()
    {
        // Advanced плывёт оба главных сета, Intermediate — только первый.
        Assert.Equal([true, false, true], WorkoutRules.ForLevel(WithMain(), Adv)!.Sets.Select(s => s.Main));
        Assert.Equal([true, false], WorkoutRules.ForLevel(WithMain(), Int)!.Sets.Select(s => s.Main));

        // Дорожка без уровня: главный сет виден (он для всех), метка доезжает.
        Assert.Equal([true, false], WorkoutRules.ForLevel(WithMain(), null)!.Sets.Select(s => s.Main));

        // Уровень, не плывущий ни одного главного сета, видит версию без главного.
        var onlyAdvMain = new WorkoutDto
        {
            Sets =
            [
                new WorkoutSetDto { Title = "обычный", Lines = [Line(1, 100, (Adv, "1:40"), (Int, "1:50"))] },
                new WorkoutSetDto { Title = "главный Adv", LevelIds = [Adv], Main = true, Lines = [Line(1, 100, (Adv, "1:40"))] },
            ],
        };
        Assert.All(WorkoutRules.ForLevel(onlyAdvMain, Int)!.Sets, s => Assert.False(s.Main));
    }

    [Fact]
    public void Main_FalseNotWritten_OldJsonReadsAsFalse_TrueRoundTrips()
    {
        var plain = WorkoutRules.Serialize(Sample());
        Assert.DoesNotContain("\"main\"", plain);

        // Документ, сохранённый до появления поля, читается как «не главный».
        var old = """{"note":null,"sets":[{"title":"סט","repeat":1,"level_ids":null,"lines":[]}]}""";
        Assert.False(WorkoutRules.Parse(old)!.Sets[0].Main);

        var stored = WorkoutRules.Serialize(WithMain());
        Assert.Contains("\"main\":true", stored);
        Assert.Equal([true, false, true], WorkoutRules.Parse(stored)!.Sets.Select(s => s.Main));
    }

    [Fact]
    public void LaneView_LevelWorkout_OnlyOnViewersLane()
    {
        var levels = new TrainingLaneView.LevelInfo[] { new(Adv, 1, "Advanced", null), new(Int, 2, "Intermediate", null) };
        var plan = new TrainingLaneView.Plan(2,
            [new TrainingLaneView.PlanLane(1, Adv, null), new TrainingLaneView.PlanLane(2, Int, null)],
            new Dictionary<int, (int? LaneNo, int OrderNo)> { [101] = (1, 0), [102] = (2, 0) },
            Sample());
        var pool = new[]
        {
            new TrainingLaneView.Person(1, 101, "A", null, "yes", null, Adv, 1, 1),
            new TrainingLaneView.Person(2, 102, "B", null, "yes", null, Int, 2, 1),
        };

        var dto = TrainingLaneView.Build(GroupLaneView.Plan, plan, null, null, levels, pool, viewerUserId: 2, namesVisible: true)!;

        Assert.Null(dto.Lanes[0].LevelWorkout);
        var mine = dto.Lanes[1].LevelWorkout!;
        Assert.Single(mine.Sets);
        Assert.Equal("1:50", mine.Sets[0].Lines[0].PerLevel![Int].SendOff);

        // Управляющему — версия уровня на каждой дорожке (режим «View» страницы группы).
        var coach = TrainingLaneView.Build(GroupLaneView.Plan, plan, null, null, levels, pool, viewerUserId: 99,
            namesVisible: true, workoutOnEveryLane: true)!;
        Assert.Equal(2, coach.Lanes[0].LevelWorkout!.Sets.Count);
        Assert.Equal("1:50", coach.Lanes[1].LevelWorkout!.Sets[0].Lines[0].PerLevel![Int].SendOff);
    }

    // ── Сервис ──────────────────────────────────────────────────────────────

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<(int GroupId, int Level, int OtherLevel)> SeedAsync(SwimmDbContext db)
    {
        var coach = new AppUser { Email = "coach@example.com", DisplayName = "coach", SecurityStamp = "s" };
        db.AppUsers.Add(coach);
        await db.SaveChangesAsync();
        var group = new HubGroup { Name = "Dolphin", Slug = "dolphin", OwnerUserId = coach.Id, IsPublic = true, TrainingSchedule = TestSchedules.EveryDay() };
        var other = new HubGroup { Name = "Other", Slug = "other", OwnerUserId = coach.Id, IsPublic = true };
        db.AddRange(group, other);
        await db.SaveChangesAsync();
        var level = new HubGroupLevel { HubGroupId = group.Id, Rank = 1, Name = "Advanced" };
        var foreign = new HubGroupLevel { HubGroupId = other.Id, Rank = 1, Name = "Fast" };
        db.HubGroupLevels.AddRange(level, foreign);
        await db.SaveChangesAsync();
        return (group.Id, level.Id, foreign.Id);
    }

    private static WorkoutDto OneSet(int levelId) => new()
    {
        Sets = [new WorkoutSetDto { Lines = [Line(4, 100, (levelId, "1:40"))] }],
    };

    [Fact]
    public async Task SaveWorkout_NeedsPlan_StoresAndPlanSaveKeepsIt()
    {
        await using var db = CreateDb(nameof(SaveWorkout_NeedsPlan_StoresAndPlanSaveKeepsIt));
        var (groupId, level, _) = await SeedAsync(db);
        var service = new LanePlanService(db);

        Assert.False((await service.SaveWorkoutAsync(groupId, At(Day), OneSet(level))).Found);

        Assert.True((await service.SaveAsync(groupId, At(Day), new LanePlanInputDto { LaneCount = 2 }, 1)).Success);
        var (found, error) = await service.SaveWorkoutAsync(groupId, At(Day), OneSet(level));
        Assert.True(found);
        Assert.Null(error);

        // PUT плана (автосохранение дорожек) workout не трогает.
        Assert.True((await service.SaveAsync(groupId, At(Day), new LanePlanInputDto { LaneCount = 3 }, 1)).Success);
        var plan = await service.GetAsync(groupId, At(Day), isManager: true);
        Assert.Equal("1:40", plan!.Workout!.Sets[0].Lines[0].PerLevel![level].SendOff);

        // Участнику целиком не отдаётся — его версия едет в lane_view.
        await service.PublishAsync(groupId, At(Day), 1);
        Assert.Null((await service.GetAsync(groupId, At(Day), isManager: false))!.Workout);

        // Пустой документ стирает.
        await service.SaveWorkoutAsync(groupId, At(Day), new WorkoutDto());
        Assert.Null((await service.GetAsync(groupId, At(Day), isManager: true))!.Workout);
    }

    [Fact]
    public async Task SaveWorkout_ForeignLevel_Rejected()
    {
        await using var db = CreateDb(nameof(SaveWorkout_ForeignLevel_Rejected));
        var (groupId, _, foreign) = await SeedAsync(db);
        var service = new LanePlanService(db);
        await service.SaveAsync(groupId, At(Day), new LanePlanInputDto { LaneCount = 2 }, 1);

        var (found, error) = await service.SaveWorkoutAsync(groupId, At(Day), OneSet(foreign));

        Assert.True(found);
        Assert.NotNull(error);
    }
}
