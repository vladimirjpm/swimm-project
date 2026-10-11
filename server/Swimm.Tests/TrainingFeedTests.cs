using Microsoft.EntityFrameworkCore;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Лента тренировок группы (docs/plans/trainings-tab-redesign-plan.md, Т1а): показатели карточки
/// (Р2 — объём одного пловца, «Avg effort» убран по Р3), строка «You» только по ★ тренера (Р4),
/// «Sets &amp; times» и пометка ключевого сета.
/// </summary>
public class TrainingFeedTests
{
    private static TrainingFeed.Repeat R(int set, int order, int swimmer, string distance, int? ms,
        string time = "1:00.00", string? intensity = null, int session = 1) =>
        new(session, set, order, swimmer, $"שם {swimmer}", $"Name {swimmer}", distance, "Freestyle", time, ms, intensity);

    // ── TrainingFeed (чистые функции) ───────────────────────────────────────

    [Fact]
    public void Stats_VolumeOfOneSwimmer_UniqueRepeats_LongestLaneWins()
    {
        // Сет 1: 4×100 у двоих — 400 м, а не 800. Сет 2, повтор 1: дорожки плыли 50 и 100 — берём 100.
        var repeats = new[]
        {
            R(1, 1, 10, "100", 70_000), R(1, 2, 10, "100", 71_000), R(1, 3, 10, "100", 72_000), R(1, 4, 10, "100", 73_000),
            R(1, 1, 11, "100", 80_000), R(1, 2, 11, "100", 81_000), R(1, 3, 11, "100", 82_000), R(1, 4, 11, "100", 83_000),
            R(2, 1, 10, "100m", 75_000), R(2, 1, 12, "50", 40_000),
        };

        var stats = TrainingFeed.Stats(repeats);

        Assert.Equal((500, 3), (stats!.Meters, stats.Swimmers));
    }

    [Fact]
    public void Stats_WithoutTimes_IsNull_AndUntimedRepeatsDontCount()
    {
        Assert.Null(TrainingFeed.Stats([R(1, 1, 10, "400", null), R(1, 2, 10, "200", 0)]));

        // Разминка без времён в объём не попадает, и пловец только с ней — не «записан».
        var stats = TrainingFeed.Stats([R(1, 1, 10, "400", null), R(2, 1, 11, "100", 70_000)]);
        Assert.Equal((100, 1), (stats!.Meters, stats.Swimmers));
    }

    [Fact]
    public void MyKey_NoStar_NoRow_EvenWithMyTimes()
    {
        var repeats = new[] { R(1, 1, 10, "100", 70_000) };

        Assert.Null(TrainingFeed.MyKey(repeats, keySetNo: null, [(10, "me")]));
        Assert.Null(TrainingFeed.MyKey(repeats, keySetNo: 1, []));
    }

    [Fact]
    public void MyKey_FastestRepeatInTheKeySet_MeBeforeFamily()
    {
        var repeats = new[]
        {
            R(1, 1, 10, "200", 150_000),                                  // сет 1 — не ключевой
            R(2, 1, 10, "100", 74_000, "1:14.00"), R(2, 2, 10, "100", 72_400, "1:12.40"), R(2, 3, 10, "100", null),
            R(2, 1, 20, "100", 65_000, "1:05.00"),                        // семья быстрее, но «me» первый
        };

        var key = TrainingFeed.MyKey(repeats, keySetNo: 2, [(10, "me"), (20, "family")]);

        Assert.Equal((2, 2, 10, "me", "100", "Freestyle", "1:12.40"),
            (key!.SetNo, key.OrderNo, key.SwimmerId, key.Kind, key.Distance, key.Style, key.Time));
    }

    [Fact]
    public void MyKey_MeNotInTheSet_FallsBackToFamily_NobodyThere_Null()
    {
        var repeats = new[] { R(2, 1, 20, "50", 34_250, "34.25"), R(1, 1, 10, "50", 33_000) };

        Assert.Equal((20, "family"), (TrainingFeed.MyKey(repeats, 2, [(10, "me"), (20, "family")])!.SwimmerId, "family"));
        Assert.Null(TrainingFeed.MyKey(repeats, 2, [(10, "me")]));
    }

    [Fact]
    public void Sets_OrderedBySetThenRepeat_KeyFlag_MineAndLevel_UntimedShownWithoutTime()
    {
        var repeats = new[]
        {
            R(2, 1, 11, "50", 40_000, "40.00", " v3 "),
            R(1, 2, 10, "100", 72_000), R(1, 1, 11, "100", 80_000), R(1, 1, 10, "100", 70_000),
            R(1, 3, 10, "100", null, ""),
        };
        var levels = new Dictionary<int, TrainingFeed.Level> { [10] = new(5, 1, "fast", "#ff0000") };

        var sets = TrainingFeed.Sets(repeats, keySetNo: 2, mine: new HashSet<int> { 10 }, levels);

        Assert.Equal([1, 2], sets.Select(s => s.SetNo));
        Assert.Equal([false, true], sets.Select(s => s.IsKey));
        // Внутри сета — повтор, затем время: 1.1 быстрый (10), 1.1 (11), 1.2, 1.3 без замера.
        Assert.Equal([(1, 10), (1, 11), (2, 10), (3, 10)], sets[0].Rows.Select(r => (r.OrderNo, r.SwimmerId)));
        Assert.Equal((true, "fast"), (sets[0].Rows[0].Mine, sets[0].Rows[0].Level!.Name));
        Assert.Equal((false, (LanePlanLevelDto?)null), (sets[0].Rows[1].Mine, sets[0].Rows[1].Level));
        Assert.Null(sets[0].Rows[3].Time);
        Assert.Equal("v3", sets[1].Rows[0].Intensity);
    }

    [Theory]
    [InlineData("100", 100)]
    [InlineData(" 50m ", 50)]
    [InlineData("", 0)]
    [InlineData("free", 0)]
    public void Meters_ParsesSourceDistance(string distance, int meters) =>
        Assert.Equal(meters, TrainingFeed.Meters(distance));

    // ── TrainingSessionService: лента ───────────────────────────────────────

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static TrainingSessionService Service(SwimmDbContext db) =>
        new(db, new HubGroupMediaService(db), new HubGroupTrainingRepository(db));

    private sealed record Seed(int GroupId, int OtherGroupId, int Coach, int Member, int MeSwimmer, int SessionId,
        int OtherSessionId, int EmptySessionId);

    /// <summary>
    /// Группа с одной тренировкой с временами (сет 1 — 2×100 у двоих, сет 2 — 50 у двоих), одной без
    /// времён и чужой тренировкой. Участник привязан к пловцу «A» (AppUser.SwimmerId), у «A» уровень.
    /// </summary>
    private static async Task<Seed> SeedAsync(SwimmDbContext db)
    {
        var a = new Swimmer { LastName = "כהן", FirstName = "נועה", LastNameEn = "Cohen", FirstNameEn = "Noa", Gender = "female", BirthYear = 1980 };
        var b = new Swimmer { LastName = "לוי", FirstName = "דניאל", Gender = "male", BirthYear = 1979 };
        db.AddRange(a, b, new Style { Id = 1, Name = "Freestyle" });
        await db.SaveChangesAsync();

        var coach = new AppUser { Email = "coach@example.com", DisplayName = "Coach", SecurityStamp = "s" };
        var member = new AppUser { Email = "noa@example.com", DisplayName = "Noa", SecurityStamp = "s", SwimmerId = a.Id };
        var group = new HubGroup { Name = "Dolphins", Slug = "dolphins", Owner = coach };
        var other = new HubGroup { Name = "Sharks", Slug = "sharks", Owner = coach };
        db.AddRange(coach, member, group, other);
        await db.SaveChangesAsync();

        var level = new HubGroupLevel { HubGroupId = group.Id, Rank = 1, Name = "fast", Color = "#ff0000" };
        db.Add(level);
        await db.SaveChangesAsync();
        db.Add(new HubGroupSwimmerLevel { HubGroupId = group.Id, SwimmerId = a.Id, LevelId = level.Id });

        var timed = new TrainingSession { HubGroupId = group.Id, ExternalTrainingId = "20260927", Name = "200->2x100", Date = new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc), PoolType = "25m" };
        var empty = new TrainingSession { HubGroupId = group.Id, ExternalTrainingId = "ui-1", Title = "Underwater filming", Date = new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc) };
        var foreign = new TrainingSession { HubGroupId = other.Id, ExternalTrainingId = "20260927", Date = new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc) };
        db.AddRange(timed, empty, foreign);
        await db.SaveChangesAsync();

        TrainingResult Row(int sessionId, Swimmer s, int set, int order, string distance, int ms, string time) => new()
        {
            SessionId = sessionId, SwimmerId = s.Id, StyleId = 1, Distance = distance, Gender = s.Gender ?? "",
            SetNo = set, OrderNo = order, TimeMillisecond = ms, TimeOriginal = time, Intensity = "v3",
        };
        db.TrainingResults.AddRange(
            Row(timed.Id, a, 1, 1, "100", 74_000, "1:14.00"), Row(timed.Id, a, 1, 2, "100", 72_400, "1:12.40"),
            Row(timed.Id, b, 1, 1, "100", 70_000, "1:10.00"), Row(timed.Id, b, 1, 2, "100", 69_000, "1:09.00"),
            Row(timed.Id, a, 2, 1, "50", 34_250, "34.25"), Row(timed.Id, b, 2, 1, "50", 33_000, "33.00"),
            Row(foreign.Id, a, 1, 1, "400", 300_000, "5:00.00"));
        await db.SaveChangesAsync();

        return new Seed(group.Id, other.Id, coach.Id, member.Id, a.Id, timed.Id, foreign.Id, empty.Id);
    }

    private static HubGroupMedia Link(Seed s, int sessionId, int? author, DateTime createdAt) => new()
    {
        HubGroupId = s.GroupId, TrainingId = sessionId, MediaType = "video", SourceType = "youtube",
        Url = $"https://youtu.be/{Guid.NewGuid():N}", CreatedByUserId = author, CreatedAt = createdAt,
    };

    [Fact]
    public async Task NewMedia_OthersAfterSeen_OwnNever_SeenClearsIt()
    {
        await using var db = CreateDb(nameof(NewMedia_OthersAfterSeen_OwnNever_SeenClearsIt));
        var s = await SeedAsync(db);
        var service = Service(db);
        var now = DateTime.UtcNow;
        db.HubGroupMedia.AddRange(
            Link(s, s.EmptySessionId, s.Coach, now.AddHours(-2)),     // чужое, новое
            Link(s, s.EmptySessionId, s.Member, now.AddHours(-1)),    // своё — никогда не «новое»
            Link(s, s.SessionId, s.Coach, now.AddDays(-30)));         // чужое, но старше окна
        await db.SaveChangesAsync();

        var before = await service.ListAsync(s.GroupId, s.Member, isManager: false);
        await service.MarkSeenAsync(s.GroupId, s.Member);
        await service.MarkSeenAsync(s.GroupId, s.Member);              // второе открытие — та же строка
        var after = await service.ListAsync(s.GroupId, s.Member, isManager: false);
        db.HubGroupMedia.Add(Link(s, s.SessionId, s.Coach, DateTime.UtcNow.AddMinutes(1)));
        await db.SaveChangesAsync();
        var fresh = await service.ListAsync(s.GroupId, s.Member, isManager: false);

        Assert.Equal(1, before.NewMediaCount);
        Assert.Equal([true, false], before.Sessions.Single(x => x.Id == s.EmptySessionId).Media.Select(m => m.IsNew));
        Assert.Equal(0, after.NewMediaCount);
        Assert.Equal(1, await db.HubGroupUserSeen.CountAsync());
        Assert.Equal(1, fresh.NewMediaCount);
        // Отметка своя у каждого: тренер ленту не открывал — у него новое участника.
        Assert.Equal(1, (await service.ListAsync(s.GroupId, s.Coach, isManager: true)).NewMediaCount);
    }

    [Fact]
    public async Task List_StatsOnTimedOnly_NoYouWithoutStar()
    {
        await using var db = CreateDb(nameof(List_StatsOnTimedOnly_NoYouWithoutStar));
        var s = await SeedAsync(db);

        var list = (await Service(db).ListAsync(s.GroupId, s.Member, isManager: false)).Sessions;

        var timed = list.Single(x => x.Id == s.SessionId);
        Assert.Equal((250, 2), (timed.Stats!.Meters, timed.Stats.Swimmers));
        Assert.Null(timed.KeySetNo);
        Assert.Null(timed.MyKey);
        var empty = list.Single(x => x.Id == s.EmptySessionId);
        Assert.Null(empty.Stats);
        Assert.DoesNotContain(list, x => x.Id == s.OtherSessionId);
    }

    [Fact]
    public async Task KeySet_StarsASet_ThenListShowsMyFastestThere()
    {
        await using var db = CreateDb(nameof(KeySet_StarsASet_ThenListShowsMyFastestThere));
        var s = await SeedAsync(db);
        var service = Service(db);

        var saved = await service.SetKeySetAsync(s.GroupId, s.SessionId, s.Coach, new TrainingKeySetInputDto { SetNo = 1 });
        var list = (await service.ListAsync(s.GroupId, s.Member, isManager: false)).Sessions;

        Assert.Equal(1, saved.Session!.KeySetNo);
        Assert.Equal((250, 2), (saved.Session.Stats!.Meters, saved.Session.Stats.Swimmers));
        Assert.Null(saved.Session.MyKey);                         // у тренера своих пловцов нет
        var you = list.Single(x => x.Id == s.SessionId).MyKey!;
        Assert.Equal((1, 2, s.MeSwimmer, "me", "1:12.40", "כהן נועה"), (you.SetNo, you.OrderNo, you.SwimmerId, you.Kind, you.Time, you.Name));
    }

    [Fact]
    public async Task KeySet_UnknownSet_400_ForeignSession_404_NullClears()
    {
        await using var db = CreateDb(nameof(KeySet_UnknownSet_400_ForeignSession_404_NullClears));
        var s = await SeedAsync(db);
        var service = Service(db);

        var noSuchSet = await service.SetKeySetAsync(s.GroupId, s.SessionId, s.Coach, new TrainingKeySetInputDto { SetNo = 7 });
        var noTimes = await service.SetKeySetAsync(s.GroupId, s.EmptySessionId, s.Coach, new TrainingKeySetInputDto { SetNo = 1 });
        var foreign = await service.SetKeySetAsync(s.GroupId, s.OtherSessionId, s.Coach, new TrainingKeySetInputDto { SetNo = 1 });
        await service.SetKeySetAsync(s.GroupId, s.SessionId, s.Coach, new TrainingKeySetInputDto { SetNo = 2 });
        var cleared = await service.SetKeySetAsync(s.GroupId, s.SessionId, s.Coach, new TrainingKeySetInputDto { SetNo = null });

        Assert.Equal((400, 400, 404), (noSuchSet.Status, noTimes.Status, foreign.Status));
        Assert.Null(cleared.Session!.KeySetNo);
        Assert.Null((await db.TrainingSessions.SingleAsync(x => x.Id == s.SessionId)).KeySetNo);
    }

    [Fact]
    public async Task Sets_MineAndLevel_ForeignSessionIsNull()
    {
        await using var db = CreateDb(nameof(Sets_MineAndLevel_ForeignSessionIsNull));
        var s = await SeedAsync(db);
        var service = Service(db);
        await service.SetKeySetAsync(s.GroupId, s.SessionId, s.Coach, new TrainingKeySetInputDto { SetNo = 2 });

        var sets = (await service.GetSetsAsync(s.GroupId, s.SessionId, s.Member, isManager: false))!;

        Assert.Equal((s.SessionId, 2, false), (sets.SessionId, sets.KeySetNo, sets.CanManage));
        Assert.Equal([(1, false, 4), (2, true, 2)], sets.Sets.Select(x => (x.SetNo, x.IsKey, x.Rows.Count)));
        var mine = sets.Sets.SelectMany(x => x.Rows).Where(r => r.Mine).ToList();
        Assert.Equal(3, mine.Count);
        Assert.All(mine, r => Assert.Equal(("fast", "v3"), (r.Level!.Name, r.Intensity)));
        Assert.All(sets.Sets.SelectMany(x => x.Rows).Where(r => !r.Mine), r => Assert.Null(r.Level));

        Assert.Null(await service.GetSetsAsync(s.GroupId, s.OtherSessionId, s.Member, isManager: false));
    }
}
