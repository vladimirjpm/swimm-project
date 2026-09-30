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
/// Тренировки группы как записи (docs/plans/group-events-media-plan.md, Е1): тренер заводит
/// тренировку кнопкой — и без времён («Underwater filming»), к ней потом видео. Решения Влада
/// 30.09.2026: отдельного типа «событие» нет; удалить можно только тренировку без времён; у
/// тренировки с временами дата и бассейн не меняются.
/// </summary>
public class TrainingSessionTests
{
    // «Сегодня» — среда 30.09.2026.
    private static readonly DateOnly Today = new(2026, 9, 30);

    private static TrainingSessionInputDto Input(string? date = "2026-09-30", string? title = "Underwater filming",
        string? start = null, string? note = null, string? pool = null) =>
        new() { Date = date, Title = title, Start = start, Note = note, PoolType = pool };

    // ── TrainingSessionRules ────────────────────────────────────────────────

    [Theory]
    [InlineData("30/09/2026", "Underwater filming", null)]   // не тот формат даты
    [InlineData("1999-12-31", "Underwater filming", null)]   // опечатка в годе
    [InlineData("2027-10-02", "Underwater filming", null)]   // дальше года вперёд
    [InlineData("2026-09-30", "   ", null)]                  // без названия
    [InlineData("2026-09-30", "Underwater filming", "25:00")]
    [InlineData("2026-09-30", "Underwater filming", "8pm")]
    public void Normalize_RejectsBadInput(string date, string title, string? start)
    {
        var (value, error) = TrainingSessionRules.Normalize(Input(date, title, start), Today);

        Assert.Null(value);
        Assert.NotNull(error);
    }

    [Fact]
    public void Normalize_TrimsAndKeepsOptionalEmpty()
    {
        var (value, error) = TrainingSessionRules.Normalize(
            Input("2026-10-01", "  Underwater filming ", start: " 07:05 ", note: "  ", pool: " 50m "), Today);

        Assert.Null(error);
        Assert.Equal((new DateOnly(2026, 10, 1), "07:05", "Underwater filming", (string?)null, "50m"),
            (value!.Date, value.StartTime, value.Title, value.Note, value.PoolType));
    }

    [Fact]
    public void Normalize_TitleLimit()
    {
        Assert.NotNull(TrainingSessionRules.Normalize(Input(title: new string('x', 101)), Today).Error);
        Assert.Null(TrainingSessionRules.Normalize(Input(title: new string('x', 100)), Today).Error);
    }

    // ── TrainingSessionService ──────────────────────────────────────────────

    private static TrainingSessionService Service(SwimmDbContext db) => new(db, new HubGroupMediaService(db));

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private sealed record Seed(int GroupId, int OtherGroupId, int Coach, int SeededSessionId);

    /// <summary>
    /// Группа с расписанием: среда 20:00 (одно занятие), суббота 08:00 и 10:00 (два), бассейн 50m;
    /// одна тренировка залита скриптом — со временами, 28.09. Вторая группа — чужая.
    /// </summary>
    private static async Task<Seed> SeedAsync(SwimmDbContext db)
    {
        var coach = new AppUser { Email = "coach@example.com", DisplayName = "Coach", SecurityStamp = "s" };
        var schedule = new GroupTrainingSchedule
        {
            PoolType = "50m",
            Slots =
            [
                new GroupTrainingSlot { Day = 3, Start = "20:00", End = "21:00" },
                new GroupTrainingSlot { Day = 6, Start = "08:00" },
                new GroupTrainingSlot { Day = 6, Start = "10:00" },
            ],
        };
        var group = new HubGroup { Name = "Dolphins", Slug = "dolphins", Owner = coach, TrainingSchedule = schedule.ToJson() };
        var other = new HubGroup { Name = "Sharks", Slug = "sharks", Owner = coach };
        var swimmer = new Swimmer { LastName = "A", FirstName = "Anna", Gender = "female", BirthYear = 1980 };
        db.AddRange(coach, group, other, swimmer);
        await db.SaveChangesAsync();

        var seeded = new TrainingSession
        {
            HubGroupId = group.Id, ExternalTrainingId = "20260928", Name = "200->2x100", Date = new DateTime(2026, 9, 28), PoolType = "25m",
        };
        db.TrainingSessions.Add(seeded);
        await db.SaveChangesAsync();
        db.TrainingResults.Add(new TrainingResult
        {
            SessionId = seeded.Id, SwimmerId = swimmer.Id, StyleId = 1, Distance = "100", Gender = "female", TimeOriginal = "1:30",
        });
        await db.SaveChangesAsync();
        return new Seed(group.Id, other.Id, coach.Id, seeded.Id);
    }

    [Fact]
    public async Task Create_WithoutTimes_TakesStartAndPoolFromTheSchedule()
    {
        await using var db = CreateDb(nameof(Create_WithoutTimes_TakesStartAndPoolFromTheSchedule));
        var s = await SeedAsync(db);

        var result = await Service(db).CreateAsync(s.GroupId, s.Coach, Input(), Today);

        var dto = Assert.IsType<TrainingSessionDto>(result.Session);
        // Среда — одно занятие 20:00: тренировка связана с ним (ключ RSVP «дата + начало»).
        Assert.Equal(("2026-09-30", "20:00", "Underwater filming", "50m", false, true),
            (dto.Date, dto.Start, dto.Title, dto.PoolType, dto.HasResults, dto.CreatedInApp));
        var row = await db.TrainingSessions.SingleAsync(x => x.Id == dto.Id);
        Assert.StartsWith(TrainingSessionRules.ExternalIdPrefix, row.ExternalTrainingId);
        Assert.Equal(s.Coach, row.CreatedByUserId);
        Assert.NotNull(row.CreatedAt);
        // timestamptz: Npgsql пишет только UTC (Unspecified роняет запись — InMemory этого не видит).
        Assert.Equal(DateTimeKind.Utc, row.Date.Kind);
    }

    [Fact]
    public async Task Create_TwoSlotsThatDay_StartStaysEmpty_ExplicitWins()
    {
        await using var db = CreateDb(nameof(Create_TwoSlotsThatDay_StartStaysEmpty_ExplicitWins));
        var s = await SeedAsync(db);
        var service = Service(db);

        var saturday = await service.CreateAsync(s.GroupId, s.Coach, Input("2026-10-03"), Today);
        var explicitStart = await service.CreateAsync(s.GroupId, s.Coach, Input("2026-10-03", start: "10:00", pool: "25m"), Today);

        Assert.Null(saturday.Session!.Start);                   // два занятия — не угадываем
        Assert.Equal(("10:00", "25m"), (explicitStart.Session!.Start, explicitStart.Session.PoolType));
    }

    [Fact]
    public async Task Create_CapPerDay_AndUnknownGroup()
    {
        await using var db = CreateDb(nameof(Create_CapPerDay_AndUnknownGroup));
        var s = await SeedAsync(db);
        var service = Service(db);

        for (var i = 0; i < TrainingSessionRules.MaxPerDay; i++)
            Assert.NotNull((await service.CreateAsync(s.GroupId, s.Coach, Input(title: $"T{i}"), Today)).Session);
        var over = await service.CreateAsync(s.GroupId, s.Coach, Input(title: "one more"), Today);
        var noGroup = await service.CreateAsync(9999, s.Coach, Input(), Today);

        Assert.Equal(400, over.Status);
        Assert.Equal(404, noGroup.Status);
    }

    [Fact]
    public async Task List_ShowsTrainingsWithoutTimes_WithMedia_NewestFirst_OnlyThisGroup()
    {
        await using var db = CreateDb(nameof(List_ShowsTrainingsWithoutTimes_WithMedia_NewestFirst_OnlyThisGroup));
        var s = await SeedAsync(db);
        var service = Service(db);
        var filming = (await service.CreateAsync(s.GroupId, s.Coach, Input(), Today)).Session!;
        await service.CreateAsync(s.OtherGroupId, s.Coach, Input(title: "Sharks day"), Today);
        db.HubGroupMedia.Add(new HubGroupMedia
        {
            HubGroupId = s.GroupId, TrainingId = filming.Id, MediaType = "video", SourceType = "youtube",
            Url = "https://youtu.be/abc", Caption = "Lane 1", CreatedByUserId = s.Coach,
        });
        await db.SaveChangesAsync();

        var list = (await service.ListAsync(s.GroupId, s.Coach, isManager: true)).Sessions;

        Assert.Equal([filming.Id, s.SeededSessionId], list.Select(x => x.Id));
        Assert.Equal(("Underwater filming", false, 1), (list[0].Title, list[0].HasResults, list[0].Media.Count));
        Assert.Equal("https://youtu.be/abc", list[0].Media[0].Url);
        Assert.Equal(("200->2x100", true, false, 0), (list[1].Plan, list[1].HasResults, list[1].CreatedInApp, list[1].Media.Count));
    }

    [Fact]
    public async Task Update_WithTimes_DateAndPoolLocked_TitleAndStartEditable()
    {
        await using var db = CreateDb(nameof(Update_WithTimes_DateAndPoolLocked_TitleAndStartEditable));
        var s = await SeedAsync(db);
        var service = Service(db);

        var moved = await service.UpdateAsync(s.GroupId, s.SeededSessionId, s.Coach, Input("2026-09-29", "Tuesday"), Today);
        var repooled = await service.UpdateAsync(s.GroupId, s.SeededSessionId, s.Coach, Input("2026-09-28", "Tuesday", pool: "50m"), Today);
        // Бассейн не прислали — остаётся как был, правка названия и начала проходит.
        var renamed = await service.UpdateAsync(s.GroupId, s.SeededSessionId, s.Coach, Input("2026-09-28", "Sprint day", start: "19:30"), Today);

        Assert.Equal(400, moved.Status);
        Assert.Equal(400, repooled.Status);
        Assert.Equal(("Sprint day", "19:30", "25m", true), (renamed.Session!.Title, renamed.Session.Start, renamed.Session.PoolType, renamed.Session.HasResults));
    }

    [Fact]
    public async Task Update_WithoutTimes_MovesDate_ForeignSessionIs404()
    {
        await using var db = CreateDb(nameof(Update_WithoutTimes_MovesDate_ForeignSessionIs404));
        var s = await SeedAsync(db);
        var service = Service(db);
        var filming = (await service.CreateAsync(s.GroupId, s.Coach, Input(), Today)).Session!;

        var moved = await service.UpdateAsync(s.GroupId, filming.Id, s.Coach, Input("2026-10-01", pool: "25m"), Today);
        var foreign = await service.UpdateAsync(s.OtherGroupId, filming.Id, s.Coach, Input(), Today);

        Assert.Equal(("2026-10-01", "25m"), (moved.Session!.Date, moved.Session.PoolType));
        Assert.Equal(404, foreign.Status);
    }

    [Fact]
    public async Task Delete_OnlyWithoutTimes_MediaGoesWithIt()
    {
        await using var db = CreateDb(nameof(Delete_OnlyWithoutTimes_MediaGoesWithIt));
        var s = await SeedAsync(db);
        var service = Service(db);
        var filming = (await service.CreateAsync(s.GroupId, s.Coach, Input(), Today)).Session!;
        db.HubGroupMedia.Add(new HubGroupMedia
        {
            HubGroupId = s.GroupId, TrainingId = filming.Id, MediaType = "video", SourceType = "youtube",
            Url = "https://youtu.be/abc", CreatedByUserId = s.Coach,
        });
        await db.SaveChangesAsync();

        var withTimes = await service.DeleteAsync(s.GroupId, s.SeededSessionId);
        var foreign = await service.DeleteAsync(s.OtherGroupId, filming.Id);
        var ok = await service.DeleteAsync(s.GroupId, filming.Id);

        Assert.Equal(400, withTimes.Status);
        Assert.Equal(404, foreign.Status);
        Assert.Null(ok.Error);
        Assert.False(await db.TrainingSessions.AnyAsync(x => x.Id == filming.Id));
        Assert.Empty(db.HubGroupMedia);
        Assert.True(await db.TrainingSessions.AnyAsync(x => x.Id == s.SeededSessionId));
    }

    // ── Е2: ссылки к тренировке от участников ───────────────────────────────
    // Права «участник ли вообще» проверяет контроллер; сюда он передаёт isManager. Участники тут —
    // просто другие id: сервису нужен только автор ссылки.

    private const int Member = 501;
    private const int OtherMember = 502;

    private static TrainingSessionMediaInputDto Video(string url = "https://youtu.be/abc", string? caption = "Lane 1",
        string mediaType = "video", string sourceType = "youtube") =>
        new() { MediaType = mediaType, SourceType = sourceType, Url = url, Caption = caption };

    private static async Task<(TrainingSessionService Service, Seed Seed, int FilmingId)> FilmingAsync(SwimmDbContext db)
    {
        var s = await SeedAsync(db);
        var service = Service(db);
        var filming = (await service.CreateAsync(s.GroupId, s.Coach, Input(), Today)).Session!;
        return (service, s, filming.Id);
    }

    [Fact]
    public async Task Member_AddsLink_MineAndDeleteFlagsPerViewer()
    {
        await using var db = CreateDb(nameof(Member_AddsLink_MineAndDeleteFlagsPerViewer));
        var (service, s, filming) = await FilmingAsync(db);

        var added = await service.AddMediaAsync(s.GroupId, filming, Member, isManager: false, Video());

        var m = Assert.IsType<TrainingSessionMediaDto>(added.Media);
        Assert.Equal(("video", "youtube", "https://youtu.be/abc", "Lane 1", true, true),
            (m.MediaType, m.SourceType, m.Url, m.Caption, m.Mine, m.CanDelete));
        Assert.Equal(Member, (await db.HubGroupMedia.SingleAsync()).CreatedByUserId);

        var byOther = await service.ListAsync(s.GroupId, OtherMember, isManager: false);
        var byCoach = await service.ListAsync(s.GroupId, s.Coach, isManager: true);
        var seenByOther = byOther.Sessions.Single(x => x.Id == filming).Media.Single();
        var seenByCoach = byCoach.Sessions.Single(x => x.Id == filming).Media.Single();

        Assert.Equal((false, false), (seenByOther.Mine, seenByOther.CanDelete));
        Assert.Equal((false, true), (seenByCoach.Mine, seenByCoach.CanDelete));
        Assert.Equal((false, true), (byOther.CanManage, byOther.CanAddMedia));
        Assert.Equal((true, true), (byCoach.CanManage, byCoach.CanAddMedia));
    }

    [Theory]
    [InlineData("http://youtu.be/abc", "video", "youtube")]       // только https
    [InlineData("https://youtu.be/abc", "audio", "youtube")]      // тип
    [InlineData("https://photos.app.goo.gl/x", "video", "album")] // album ⇔ album
    public async Task AddMedia_LinkChecksAreTheGroupMediaOnes(string url, string mediaType, string sourceType)
    {
        await using var db = CreateDb(nameof(AddMedia_LinkChecksAreTheGroupMediaOnes) + mediaType + sourceType + url.Length);
        var (service, s, filming) = await FilmingAsync(db);

        var result = await service.AddMediaAsync(s.GroupId, filming, Member, false, Video(url, null, mediaType, sourceType));

        Assert.Equal(400, result.Status);
        Assert.Empty(db.HubGroupMedia);
    }

    [Fact]
    public async Task AddMedia_UnknownOrForeignTraining_Is404()
    {
        await using var db = CreateDb(nameof(AddMedia_UnknownOrForeignTraining_Is404));
        var (service, s, filming) = await FilmingAsync(db);

        Assert.Equal(404, (await service.AddMediaAsync(s.GroupId, 99999, Member, false, Video())).Status);
        Assert.Equal(404, (await service.AddMediaAsync(s.OtherGroupId, filming, Member, false, Video())).Status);
    }

    [Fact]
    public async Task CoachOnlyGroup_MemberCannotAdd_CoachCan()
    {
        await using var db = CreateDb(nameof(CoachOnlyGroup_MemberCannotAdd_CoachCan));
        var (service, s, filming) = await FilmingAsync(db);
        var group = await db.HubGroups.SingleAsync(g => g.Id == s.GroupId);
        var schedule = GroupTrainingSchedule.Parse(group.TrainingSchedule);
        schedule.TrainingMedia = GroupTrainingMedia.Coach;
        group.TrainingSchedule = schedule.ToJson();
        await db.SaveChangesAsync();

        var byMember = await service.AddMediaAsync(s.GroupId, filming, Member, false, Video());
        var byCoach = await service.AddMediaAsync(s.GroupId, filming, s.Coach, true, Video());

        Assert.Equal(403, byMember.Status);
        Assert.NotNull(byCoach.Media);
        Assert.False((await service.ListAsync(s.GroupId, Member, false)).CanAddMedia);
    }

    [Fact]
    public async Task DeleteMedia_AuthorOwn_CoachAny_OthersNot()
    {
        await using var db = CreateDb(nameof(DeleteMedia_AuthorOwn_CoachAny_OthersNot));
        var (service, s, filming) = await FilmingAsync(db);
        var a = (await service.AddMediaAsync(s.GroupId, filming, Member, false, Video("https://youtu.be/a"))).Media!;
        var b = (await service.AddMediaAsync(s.GroupId, filming, Member, false, Video("https://youtu.be/b"))).Media!;
        // Аккаунт автора удалён (SetNull) — удалить может только управляющий.
        db.HubGroupMedia.Add(new HubGroupMedia
        {
            HubGroupId = s.GroupId, TrainingId = filming, MediaType = "video", SourceType = "youtube",
            Url = "https://youtu.be/orphan", CreatedByUserId = null,
        });
        await db.SaveChangesAsync();
        var orphan = await db.HubGroupMedia.SingleAsync(m => m.CreatedByUserId == null);

        var byOther = await service.DeleteMediaAsync(s.GroupId, filming, a.Id, OtherMember, false);
        var orphanByMember = await service.DeleteMediaAsync(s.GroupId, filming, orphan.Id, Member, false);
        var wrongSession = await service.DeleteMediaAsync(s.GroupId, s.SeededSessionId, a.Id, Member, false);
        var own = await service.DeleteMediaAsync(s.GroupId, filming, a.Id, Member, false);
        var byCoach = await service.DeleteMediaAsync(s.GroupId, filming, b.Id, s.Coach, true);
        var orphanByCoach = await service.DeleteMediaAsync(s.GroupId, filming, orphan.Id, s.Coach, true);

        Assert.Equal((403, 403, 404), (byOther.Status, orphanByMember.Status, wrongSession.Status));
        Assert.Equal(((string?)null, (string?)null, (string?)null), (own.Error, byCoach.Error, orphanByCoach.Error));
        Assert.Empty(db.HubGroupMedia);
    }

    [Fact]
    public async Task AddMedia_Caps_PerMemberAndPerTraining()
    {
        await using var db = CreateDb(nameof(AddMedia_Caps_PerMemberAndPerTraining));
        var (service, s, filming) = await FilmingAsync(db);

        for (var i = 0; i < TrainingSessionRules.MaxMediaPerMember; i++)
            Assert.NotNull((await service.AddMediaAsync(s.GroupId, filming, Member, false, Video($"https://youtu.be/m{i}"))).Media);
        var memberOver = await service.AddMediaAsync(s.GroupId, filming, Member, false, Video("https://youtu.be/over"));
        Assert.Equal(400, memberOver.Status);

        // Управляющего потолок участника не держит — только общий потолок тренировки.
        var left = TrainingSessionRules.MaxMediaPerSession - TrainingSessionRules.MaxMediaPerMember;
        for (var i = 0; i < left; i++)
            Assert.NotNull((await service.AddMediaAsync(s.GroupId, filming, s.Coach, true, Video($"https://youtu.be/c{i}"))).Media);
        var sessionOver = await service.AddMediaAsync(s.GroupId, filming, s.Coach, true, Video("https://youtu.be/full"));

        Assert.Equal(400, sessionOver.Status);
        Assert.Equal(TrainingSessionRules.MaxMediaPerSession, await db.HubGroupMedia.CountAsync());
    }

    [Fact]
    public void Schedule_TrainingMedia_DefaultsToMembers_UnknownIsMembers()
    {
        Assert.Equal(GroupTrainingMedia.Members, new GroupTrainingSchedule().EffectiveTrainingMedia);
        Assert.Equal(GroupTrainingMedia.Members, new GroupTrainingSchedule { TrainingMedia = "nobody" }.EffectiveTrainingMedia);

        var coachOnly = GroupTrainingSchedule.Parse(new GroupTrainingSchedule { TrainingMedia = "coach" }.ToJson());
        Assert.Equal(GroupTrainingMedia.Coach, coachOnly.EffectiveTrainingMedia);
    }
}
