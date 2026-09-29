using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Жалобы «Report» на медиа (Р62, docs/data-integrity.md): кто может пожаловаться, порог открытых
/// жалоб прячет медиа со ВСЕХ витрин (включая страницу группы), решение админа сайта «оставить» /
/// «снять», тренер видит число и причины без имён.
/// </summary>
public class MediaReportTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class SettingsStub(int threshold) : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) =>
            key == MediaReportRules.HideThresholdKey ? (T)(object)threshold : fallback;
        public bool Update(string key, string newValue) => true;
    }

    private sealed class Seed
    {
        public AppUser Publisher = null!, Coach = null!, A = null!, B = null!, C = null!;
        public Swimmer Swimmer = null!;
        public HubGroup Group = null!;
        public UserMedia Media = null!;
        public int PublicationId;
    }

    /// <summary>Родитель публикует видео заплыва в доверенную группу уровнем public; тренер одобряет.</summary>
    private static async Task<Seed> SeedAsync(SwimmDbContext db, string level = "public")
    {
        AppUser U(string email) => new() { Email = email, DisplayName = email, SecurityStamp = "s" };
        var s = new Seed
        {
            Publisher = U("parent@example.com"), Coach = U("coach@example.com"),
            A = U("a@example.com"), B = U("b@example.com"), C = U("c@example.com"),
            Swimmer = new Swimmer { LastName = "ברנצב", FirstName = "סבינה", BirthYear = 2014 },
        };
        var comp = new Competition { Name = "Meet", Date = "16/07/2026", PoolType = "25m" };
        var style = new Style { Name = "Freestyle" };
        var club = new Club { Name = "הפועל דולפין נתניה" };
        db.AddRange(s.Publisher, s.Coach, s.A, s.B, s.C, s.Swimmer, comp, style, club);
        await db.SaveChangesAsync();

        var result = new ResultRecord
        {
            SwimmerId = s.Swimmer.Id, ClubId = club.Id, CompetitionId = comp.Id, StyleId = style.Id,
            Distance = "50", Gender = "female", CompetitionDate = new DateTime(2026, 7, 16)
        };
        db.Results.Add(result);
        s.Group = new HubGroup { Name = "Dolphin", Slug = "dolphin", OwnerUserId = s.Coach.Id, IsTrusted = true };
        db.HubGroups.Add(s.Group);
        await db.SaveChangesAsync();

        db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = s.Group.Id, SwimmerId = s.Swimmer.Id });
        db.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = s.Group.Id, UserId = s.Publisher.Id, Status = HubGroupUserMemberStatus.Active });
        s.Media = new UserMedia
        {
            UserId = s.Publisher.Id, SwimmerId = s.Swimmer.Id, ResultId = result.Id, CompetitionId = comp.Id,
            Level = "result", MediaType = "video", SourceType = "youtube", Url = "https://www.youtube.com/watch?v=sabina50"
        };
        db.UserMedia.Add(s.Media);
        await db.SaveChangesAsync();

        var svc = new UserMediaPublicationService(db);
        var submit = await svc.SubmitAsync(s.Publisher.Id, s.Media.Id,
            new SubmitPublicationRequest { TargetType = UserMediaPublicationTarget.Group, TargetId = s.Group.Id, Level = level },
            isPrivileged: false);
        Assert.True(submit.Success, submit.Error);
        s.PublicationId = submit.Publication!.Id;
        Assert.True(await svc.DecideAsync(UserMediaPublicationTarget.Group, s.Group.Id, s.PublicationId, approve: true, decidedByUserId: s.Coach.Id));
        return s;
    }

    private static MediaReportService Service(SwimmDbContext db, int threshold = 2) =>
        new(db, new SettingsStub(threshold), Mock.Of<IAdminAuditService>());

    private static SubmitMediaReportRequest Why(string reason, string? comment = null) => new() { Reason = reason, Comment = comment };

    private static async Task<bool> GuestSeesOnSwimmerPageAsync(SwimmDbContext db, Seed s) =>
        (await new UserMediaPublicationService(db).GetVisibleForSwimmerAsync(s.Swimmer.Id, null, false))
            .Any(v => v.MediaId == s.Media.Id);

    private static async Task<bool> OnGroupGalleryAsync(SwimmDbContext db, Seed s) =>
        (await new UserMediaPublicationService(db).GetApprovedForGroupAsync(s.Group.Id, "public"))
            .Any(v => v.MediaId == s.Media.Id);

    [Fact]
    public async Task Report_OwnMedia_Refused()
    {
        await using var db = CreateDb(nameof(Report_OwnMedia_Refused));
        var s = await SeedAsync(db);

        var r = await Service(db).ReportAsync(s.Publisher.Id, s.Media.Id, Why(MediaReportRules.ReasonSpam), false);
        Assert.Equal(MediaReportOutcome.OwnMedia, r.Outcome);
        Assert.Empty(db.MediaReports);
    }

    [Fact]
    public async Task Report_MediaTheReporterCannotSee_NotFound()
    {
        await using var db = CreateDb(nameof(Report_MediaTheReporterCannotSee_NotFound));
        var s = await SeedAsync(db, level: "members");

        // Members-видео постороннему не видно — жалоба на него по id не проходит (иначе перебором
        // id можно было бы прятать чужое закрытое).
        var r = await Service(db).ReportAsync(s.A.Id, s.Media.Id, Why(MediaReportRules.ReasonSpam), false);
        Assert.Equal(MediaReportOutcome.NotFound, r.Outcome);
    }

    [Theory]
    [InlineData("nonsense", null)]
    [InlineData(MediaReportRules.ReasonOther, null)]
    [InlineData(MediaReportRules.ReasonOther, "   ")]
    public async Task Report_BadReasonOrEmptyOther_Invalid(string reason, string? comment)
    {
        await using var db = CreateDb(nameof(Report_BadReasonOrEmptyOther_Invalid) + reason + comment);
        var s = await SeedAsync(db);

        var r = await Service(db).ReportAsync(s.A.Id, s.Media.Id, Why(reason, comment), false);
        Assert.Equal(MediaReportOutcome.Invalid, r.Outcome);
    }

    [Fact]
    public async Task Report_CommentOverLimit_Invalid()
    {
        await using var db = CreateDb(nameof(Report_CommentOverLimit_Invalid));
        var s = await SeedAsync(db);

        var r = await Service(db).ReportAsync(s.A.Id, s.Media.Id,
            Why(MediaReportRules.ReasonOther, new string('x', MediaReportRules.MaxCommentLength + 1)), false);
        Assert.Equal(MediaReportOutcome.Invalid, r.Outcome);
    }

    [Fact]
    public async Task Report_SameAccountTwice_CountsOnce_DoesNotHide()
    {
        await using var db = CreateDb(nameof(Report_SameAccountTwice_CountsOnce_DoesNotHide));
        var s = await SeedAsync(db);
        var svc = Service(db, threshold: 2);

        Assert.False((await svc.ReportAsync(s.A.Id, s.Media.Id, Why(MediaReportRules.ReasonSpam), false)).AlreadyReported);
        Assert.True((await svc.ReportAsync(s.A.Id, s.Media.Id, Why(MediaReportRules.ReasonInappropriate), false)).AlreadyReported);

        Assert.Single(db.MediaReports);
        Assert.True(await GuestSeesOnSwimmerPageAsync(db, s));
    }

    [Fact]
    public async Task Threshold_HidesEverywhere_IncludingGroupPage_OwnerStillSees()
    {
        await using var db = CreateDb(nameof(Threshold_HidesEverywhere_IncludingGroupPage_OwnerStillSees));
        var s = await SeedAsync(db);
        var svc = Service(db, threshold: 2);

        await svc.ReportAsync(s.A.Id, s.Media.Id, Why(MediaReportRules.ReasonWrongSwimmer), false);
        Assert.True(await GuestSeesOnSwimmerPageAsync(db, s));   // одна жалоба — ниже порога

        await svc.ReportAsync(s.B.Id, s.Media.Id, Why(MediaReportRules.ReasonPrivacy), false);
        Assert.Equal(MediaReportRules.StateUnderReview, (await db.UserMedia.AsNoTracking().SingleAsync()).ModerationState);

        var pubs = new UserMediaPublicationService(db);
        Assert.False(await GuestSeesOnSwimmerPageAsync(db, s));
        Assert.False(await OnGroupGalleryAsync(db, s));
        // Тренер группы и админ сайта через публикации тоже не видят — спрятано везде.
        Assert.DoesNotContain(await pubs.GetVisibleForSwimmerAsync(s.Swimmer.Id, s.Coach.Id, false), v => v.MediaId == s.Media.Id);
        Assert.DoesNotContain(await pubs.GetVisibleForSwimmerAsync(s.Swimmer.Id, s.C.Id, true), v => v.MediaId == s.Media.Id);
        // Владелец видит своё.
        Assert.Contains(await pubs.GetVisibleForSwimmerAsync(s.Swimmer.Id, s.Publisher.Id, false), v => v.MediaId == s.Media.Id && v.IsMine);
        // Лайк спрятанного — нельзя.
        Assert.Null(await new ReactionRepository(db).SetLikeAsync(s.C.Id, s.Media.Id, on: true, isSiteAdmin: false));
        // Повторно опубликовать до решения — нельзя.
        var resubmit = await pubs.SubmitAsync(s.Publisher.Id, s.Media.Id,
            new SubmitPublicationRequest { TargetType = UserMediaPublicationTarget.Group, TargetId = s.Group.Id, Level = "members" }, false);
        Assert.False(resubmit.Success);
    }

    [Fact]
    public async Task Keep_ReturnsMedia_ClosesReports_NewReportsCountFromZero()
    {
        await using var db = CreateDb(nameof(Keep_ReturnsMedia_ClosesReports_NewReportsCountFromZero));
        var s = await SeedAsync(db);
        var svc = Service(db, threshold: 2);
        await svc.ReportAsync(s.A.Id, s.Media.Id, Why(MediaReportRules.ReasonSpam), false);
        await svc.ReportAsync(s.B.Id, s.Media.Id, Why(MediaReportRules.ReasonSpam), false);
        Assert.False(await GuestSeesOnSwimmerPageAsync(db, s));

        Assert.True(await svc.DecideAsync(s.Media.Id, keep: true, adminUserId: s.C.Id));
        db.ChangeTracker.Clear();

        Assert.True(await GuestSeesOnSwimmerPageAsync(db, s));
        Assert.True(await OnGroupGalleryAsync(db, s));
        Assert.All(db.MediaReports, r => Assert.Equal(MediaReportRules.StatusKept, r.Status));
        Assert.Equal(0, await svc.CountOpenAsync());

        // Закрытые жалобы порог не добивают: одна новая — ниже порога 2.
        await svc.ReportAsync(s.C.Id, s.Media.Id, Why(MediaReportRules.ReasonSpam), false);
        Assert.True(await GuestSeesOnSwimmerPageAsync(db, s));
    }

    [Fact]
    public async Task Remove_RejectsPublications_BlocksResubmit()
    {
        await using var db = CreateDb(nameof(Remove_RejectsPublications_BlocksResubmit));
        var s = await SeedAsync(db);
        var svc = Service(db, threshold: 5);
        await svc.ReportAsync(s.A.Id, s.Media.Id, Why(MediaReportRules.ReasonInappropriate), false);

        // Админ снимает и до порога — решение за ним.
        Assert.True(await svc.DecideAsync(s.Media.Id, keep: false, adminUserId: s.C.Id));
        db.ChangeTracker.Clear();

        Assert.Equal(MediaReportRules.StateRemoved, (await db.UserMedia.SingleAsync()).ModerationState);
        Assert.Equal(UserMediaPublicationStatus.Rejected, (await db.UserMediaPublications.SingleAsync()).Status);
        Assert.Equal(MediaReportRules.StatusRemoved, (await db.MediaReports.SingleAsync()).Status);
        Assert.False(await GuestSeesOnSwimmerPageAsync(db, s));

        var resubmit = await new UserMediaPublicationService(db).SubmitAsync(s.Publisher.Id, s.Media.Id,
            new SubmitPublicationRequest { TargetType = UserMediaPublicationTarget.Group, TargetId = s.Group.Id, Level = "public" }, false);
        Assert.False(resubmit.Success);
    }

    /// <summary>
    /// Владелец удаляет медиа с жалобами сам — жалобы уходят каскадом, поэтому след для админа
    /// снимается ДО удаления (хвост 8.15). Без жалоб и у чужого медиа следа нет.
    /// </summary>
    [Fact]
    public async Task OwnerDelete_CapturesReportsTrail_AndLogsIt()
    {
        await using var db = CreateDb(nameof(OwnerDelete_CapturesReportsTrail_AndLogsIt));
        var s = await SeedAsync(db);
        var audit = new Mock<IAdminAuditService>();
        var svc = new MediaReportService(db, new SettingsStub(2), audit.Object);

        Assert.Null(await svc.CaptureBeforeOwnerDeleteAsync(s.Publisher.Id, s.Media.Id));  // жалоб нет

        await svc.ReportAsync(s.A.Id, s.Media.Id, Why(MediaReportRules.ReasonSpam), false);
        await svc.ReportAsync(s.B.Id, s.Media.Id, Why(MediaReportRules.ReasonPrivacy), false);
        Assert.True(await svc.DecideAsync(s.Media.Id, keep: true, adminUserId: s.C.Id));
        await svc.ReportAsync(s.C.Id, s.Media.Id, Why(MediaReportRules.ReasonSpam), false);

        Assert.Null(await svc.CaptureBeforeOwnerDeleteAsync(s.A.Id, s.Media.Id));  // не владелец

        var trail = await svc.CaptureBeforeOwnerDeleteAsync(s.Publisher.Id, s.Media.Id);
        Assert.NotNull(trail);
        Assert.Equal(1, trail!.Open);
        Assert.Equal(2, trail.Decided);
        Assert.Equal(2, trail.Reasons[MediaReportRules.ReasonSpam]);
        Assert.Equal(3, trail.ReporterUserIds.Count);

        await svc.LogOwnerDeleteAsync(trail);
        audit.Verify(a => a.LogAsync("media.report.owner-delete", "UserMedia", s.Media.Id.ToString(),
            It.Is<string>(t => t.Contains("открытых 1") && t.Contains("разобранных 2")),
            trail, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CoachInbox_ShowsReasonCounts_WithoutReporters()
    {
        await using var db = CreateDb(nameof(CoachInbox_ShowsReasonCounts_WithoutReporters));
        var s = await SeedAsync(db);
        var svc = Service(db, threshold: 10);
        await svc.ReportAsync(s.A.Id, s.Media.Id, Why(MediaReportRules.ReasonWrongSwimmer), false);
        await svc.ReportAsync(s.B.Id, s.Media.Id, Why(MediaReportRules.ReasonWrongSwimmer), false);
        await svc.ReportAsync(s.C.Id, s.Media.Id, Why(MediaReportRules.ReasonOther, "secret text"), false);

        var item = Assert.Single(await new UserMediaPublicationService(db).GetForGroupAsync(s.Group.Id));
        Assert.Equal(2, item.OpenReports[MediaReportRules.ReasonWrongSwimmer]);
        Assert.Equal(1, item.OpenReports[MediaReportRules.ReasonOther]);
        // В строке тренера нет ни email пожаловавшихся, ни текста — только причина и число.
        var json = System.Text.Json.JsonSerializer.Serialize(item);
        Assert.DoesNotContain("a@example.com", json);
        Assert.DoesNotContain("secret text", json);

        // Админ сайта в очереди видит всё.
        var queue = Assert.Single(await svc.GetQueueAsync(open: true));
        Assert.Contains(queue.Reports, r => r.ReporterEmail == "c@example.com" && r.Comment == "secret text");
    }
}
