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
/// Лента «Since publishing» опубликованного плана (docs/plans/trainings-coach-flow-plan.md, CF8):
/// публикация начинает её, Unpublish стирает; поздний ответ и перенос тренером — строки; частые
/// переносы одного человека склеиваются; Undo тоста убирает строку; черновик ленты не пишет.
/// </summary>
public class LanePlanEventsTests
{
    private static readonly DateOnly Tue = new(2026, 9, 29);
    private static readonly LanePlanKey Evening = LanePlanKey.Session(Tue, "20:00");
    private static readonly DateTime MondayNoon = new(2026, 9, 28, 12, 0, 0);

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    // ── Правила ─────────────────────────────────────────────────────────────

    [Fact]
    public void Place_EncodesNotTodayUnassignedAndLane()
    {
        Assert.Null(LanePlanEventRules.Place(false, 3));
        Assert.Equal(0, LanePlanEventRules.Place(true, null));
        Assert.Equal(3, LanePlanEventRules.Place(true, 3));
    }

    [Fact]
    public void MergeCoachMove_SameCoachRecentMove_UpdatesOrRemoves_OtherwiseAdds()
    {
        var now = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);
        LanePlanEvent Last(string kind, int? actor, int minutesAgo, int? from = 1) =>
            new() { Kind = kind, ActorUserId = actor, At = now.AddMinutes(-minutesAgo), FromLane = from, ToLane = 2 };

        Assert.Equal(LanePlanEventRules.MoveMerge.Add, LanePlanEventRules.MergeCoachMove(null, 7, 3, now));
        Assert.Equal(LanePlanEventRules.MoveMerge.Update, LanePlanEventRules.MergeCoachMove(Last(LanePlanEventKind.Moved, 7, 2), 7, 3, now));
        // Вернул туда, откуда переносил, — строки нет.
        Assert.Equal(LanePlanEventRules.MoveMerge.Remove, LanePlanEventRules.MergeCoachMove(Last(LanePlanEventKind.Moved, 7, 2), 7, 1, now));
        // Другой тренер, давно, или прошлая строка — ответ: новая строка.
        Assert.Equal(LanePlanEventRules.MoveMerge.Add, LanePlanEventRules.MergeCoachMove(Last(LanePlanEventKind.Moved, 8, 2), 7, 3, now));
        Assert.Equal(LanePlanEventRules.MoveMerge.Add, LanePlanEventRules.MergeCoachMove(Last(LanePlanEventKind.Moved, 7, 11), 7, 3, now));
        Assert.Equal(LanePlanEventRules.MoveMerge.Add, LanePlanEventRules.MergeCoachMove(Last(LanePlanEventKind.Going, 7, 2), 7, 3, now));
    }

    // ── Сервис ──────────────────────────────────────────────────────────────

    private sealed record Seed(int GroupId, int Coach, int Anna, int AnnaSwimmer, int Gil, int Dan, int Fast);

    /// <summary>
    /// Вторник 20:00. Anna — участница-аккаунт, стоит пловчихой Anna (Fast); Gil — пловец без аккаунта
    /// (Fast); Dan — пловец без аккаунта (Fast). План: 3 дорожки Fast, Anna — на 1.
    /// </summary>
    private static async Task<Seed> SeedAsync(SwimmDbContext db)
    {
        AppUser U(string name) => new() { Email = $"{name}@example.com", DisplayName = name, SecurityStamp = "s" };
        var coach = U("Coach");
        var anna = U("Anna");
        var annaSwimmer = new Swimmer { LastName = "A", FirstName = "Anna", BirthYear = 1980 };
        var gil = new Swimmer { LastName = "G", FirstName = "Gil", BirthYear = 1975 };
        var dan = new Swimmer { LastName = "D", FirstName = "Dan", BirthYear = 1976 };
        var schedule = new GroupTrainingSchedule { Slots = [new GroupTrainingSlot { Day = 2, Start = "20:00", End = "21:00" }] };
        var group = new HubGroup { Name = "Dolphins", Slug = "dolphins", Owner = coach, TrainingSchedule = schedule.ToJson() };
        db.AddRange(coach, anna, annaSwimmer, gil, dan, group);
        await db.SaveChangesAsync();

        var fast = new HubGroupLevel { HubGroupId = group.Id, Rank = 1, Name = "Fast" };
        db.HubGroupLevels.Add(fast);
        db.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = group.Id, UserId = anna.Id, SwimmerId = annaSwimmer.Id });
        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = annaSwimmer.Id, SortOrder = 1 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = gil.Id, SortOrder = 2 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = dan.Id, SortOrder = 3 });
        await db.SaveChangesAsync();
        db.HubGroupSwimmerLevels.AddRange(
            new HubGroupSwimmerLevel { HubGroupId = group.Id, SwimmerId = annaSwimmer.Id, LevelId = fast.Id },
            new HubGroupSwimmerLevel { HubGroupId = group.Id, SwimmerId = gil.Id, LevelId = fast.Id },
            new HubGroupSwimmerLevel { HubGroupId = group.Id, SwimmerId = dan.Id, LevelId = fast.Id });
        await db.SaveChangesAsync();

        var s = new Seed(group.Id, coach.Id, anna.Id, annaSwimmer.Id, gil.Id, dan.Id, fast.Id);
        Assert.True((await new LanePlanService(db).SaveAsync(group.Id, Evening, Plan(s, (annaSwimmer.Id, 1)), coach.Id)).Success);
        return s;
    }

    private static LanePlanInputDto Plan(Seed s, params (int SwimmerId, int? LaneNo)[] swimmers) => new()
    {
        LaneCount = 3,
        ReservedLanes = 0,
        Lanes = Enumerable.Range(1, 3).Select(n => new LanePlanLaneInputDto { LaneNo = n, LevelId = s.Fast }).ToList(),
        Swimmers = swimmers.Select(w => new LanePlanSwimmerInputDto { SwimmerId = w.SwimmerId, LaneNo = w.LaneNo }).ToList(),
    };

    private static Task<List<LanePlanEventDto>?> EventsAsync(SwimmDbContext db, Seed s) =>
        new LanePlanService(db).GetEventsAsync(s.GroupId, Evening, s.Coach);

    private static Task<TrainingRsvpSaveResult> AnswerAsync(SwimmDbContext db, Seed s, TrainingRsvpInputDto input, int actor) =>
        new TrainingRsvpService(db).SetAsync(s.GroupId, Evening.ToString(), actor, actor == s.Coach, input, MondayNoon);

    [Fact]
    public async Task Draft_WritesNothing_PublishStarts_UnpublishClears_RepublishRestarts()
    {
        await using var db = CreateDb(nameof(Draft_WritesNothing_PublishStarts_UnpublishClears_RepublishRestarts));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);

        // Черновик: правки ленту не пишут.
        await service.SaveAsync(s.GroupId, Evening, Plan(s, (s.AnnaSwimmer, 2), (s.Gil, 1)), s.Coach);
        Assert.Empty((await EventsAsync(db, s))!);

        Assert.True((await service.PublishAsync(s.GroupId, Evening, s.Coach)).Success);
        var published = Assert.Single((await EventsAsync(db, s))!);
        Assert.Equal((LanePlanEventKind.Published, (int?)2, true, "Coach"), (published.Kind, published.OnLanes, published.ByMe, published.ActorName));

        await service.SaveAsync(s.GroupId, Evening, Plan(s, (s.AnnaSwimmer, 3), (s.Gil, 1)), s.Coach);
        Assert.Equal(2, (await EventsAsync(db, s))!.Count);

        // Unpublish — ленты нет; повторная публикация — снова одна строка.
        Assert.True(await service.SetStatusAsync(s.GroupId, Evening, LanePlanStatus.Draft, s.Coach));
        Assert.Empty((await EventsAsync(db, s))!);
        await service.PublishAsync(s.GroupId, Evening, s.Coach);
        Assert.Equal(LanePlanEventKind.Published, Assert.Single((await EventsAsync(db, s))!).Kind);

        // У занятия без плана ленты нет вовсе.
        Assert.Null(await service.GetEventsAsync(s.GroupId, LanePlanKey.Session(Tue.AddDays(7), "20:00"), s.Coach));
    }

    [Fact]
    public async Task LateAnswers_AreLines_NewestFirst_WithWhoAnswered()
    {
        await using var db = CreateDb(nameof(LateAnswers_AreLines_NewestFirst_WithWhoAnswered));
        var s = await SeedAsync(db);
        await new LanePlanService(db).PublishAsync(s.GroupId, Evening, s.Coach);

        // Gil — «Going» от тренера: встаёт на дорожку 2 (свободная Fast с меньшим номером).
        Assert.Null((await AnswerAsync(db, s, new TrainingRsvpInputDto { Answer = "yes", SwimmerId = s.Gil }, s.Coach)).Error);
        // Anna сама «Can't come»: снята с дорожки 1.
        Assert.Null((await AnswerAsync(db, s, new TrainingRsvpInputDto { Answer = "no" }, s.Anna)).Error);

        var events = (await EventsAsync(db, s))!;
        Assert.Equal([LanePlanEventKind.CantCome, LanePlanEventKind.Going, LanePlanEventKind.Published], events.Select(e => e.Kind));
        var cant = events[0];
        Assert.Equal((s.AnnaSwimmer, (int?)1, (int?)null, false, false, "Anna"),
            (cant.SwimmerId!.Value, cant.FromLane, cant.ToLane, cant.ByCoach, cant.ByMe, cant.ActorName));
        Assert.Equal("A Anna", cant.Name);
        var going = events[1];
        Assert.Equal((s.Gil, (int?)null, (int?)2, true, true), (going.SwimmerId!.Value, going.FromLane, going.ToLane, going.ByCoach, going.ByMe));
    }

    [Fact]
    public async Task CoachMoves_AreLines_RepeatedMovesMerge_MovingBackRemoves()
    {
        await using var db = CreateDb(nameof(CoachMoves_AreLines_RepeatedMovesMerge_MovingBackRemoves));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);
        await service.SaveAsync(s.GroupId, Evening, Plan(s, (s.AnnaSwimmer, 1), (s.Gil, 2)), s.Coach);
        await service.PublishAsync(s.GroupId, Evening, s.Coach);

        // Anna 1 → 3, потом 3 → 2: одна строка «1 → 2». Gil снят с плана, Dan поставлен на 3.
        await service.SaveAsync(s.GroupId, Evening, Plan(s, (s.AnnaSwimmer, 3), (s.Gil, 2)), s.Coach);
        await service.SaveAsync(s.GroupId, Evening, Plan(s, (s.AnnaSwimmer, 2), (s.Dan, 3)), s.Coach);
        var moves = (await EventsAsync(db, s))!.Where(e => e.Kind == LanePlanEventKind.Moved)
            .ToDictionary(e => e.SwimmerId!.Value, e => (e.FromLane, e.ToLane));
        Assert.Equal(3, moves.Count);
        Assert.Equal(((int?)1, (int?)2), moves[s.AnnaSwimmer]);
        Assert.Equal(((int?)2, (int?)null), moves[s.Gil]);
        Assert.Equal(((int?)null, (int?)3), moves[s.Dan]);

        // Anna вернулась на 1 — её строки больше нет.
        await service.SaveAsync(s.GroupId, Evening, Plan(s, (s.AnnaSwimmer, 1), (s.Dan, 3)), s.Coach);
        Assert.DoesNotContain(s.AnnaSwimmer, (await EventsAsync(db, s))!.Select(e => e.SwimmerId));
    }

    [Fact]
    public async Task UndoOfAnswer_RemovesItsLine()
    {
        await using var db = CreateDb(nameof(UndoOfAnswer_RemovesItsLine));
        var s = await SeedAsync(db);
        await new LanePlanService(db).PublishAsync(s.GroupId, Evening, s.Coach);

        var going = (await AnswerAsync(db, s, new TrainingRsvpInputDto { Answer = "yes", SwimmerId = s.Dan }, s.Coach)).Rsvp!;
        Assert.Equal(2, (await EventsAsync(db, s))!.Count);

        await AnswerAsync(db, s, new TrainingRsvpInputDto { Answer = null, SwimmerId = s.Dan, RestoreLane = going.LaneChange!.Before }, s.Coach);
        Assert.Equal(LanePlanEventKind.Published, Assert.Single((await EventsAsync(db, s))!).Kind);
    }
}
