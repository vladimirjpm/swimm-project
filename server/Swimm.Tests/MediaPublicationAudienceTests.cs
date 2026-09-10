using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Кому видно одобренное members-видео — одно правило (<c>MediaPublicationAudience</c>) на
/// протокол, страницу пловца и лайк. Аудитория та же, что у ленты members на странице группы:
/// активный участник-аккаунт, владелец, админ группы, админ сайта.
///
/// Регрессия 10.09.2026: админ группы, не вступивший в неё участником, видел разбор на
/// странице группы, но не в протоколе и не на странице пловца; а лайк пускал участника с
/// висящей заявкой, хотя само видео ему не показывали.
/// </summary>
public class MediaPublicationAudienceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class Seed
    {
        public AppUser Publisher = null!, Owner = null!, GroupAdmin = null!, Pending = null!, Stranger = null!;
        public Swimmer Swimmer = null!;
        public Competition Comp = null!;
        public UserMedia Media = null!;
    }

    /// <summary>
    /// Родитель-участник публикует видео заплыва в группу уровнем members; владелец одобряет.
    /// Владелец и админ группы — НЕ участники-аккаунты (так и бывает у тренера-админа).
    /// </summary>
    private static async Task<Seed> SeedAsync(SwimmDbContext db)
    {
        AppUser U(string email) => new() { Email = email, DisplayName = email, SecurityStamp = "s" };
        var s = new Seed
        {
            Publisher = U("parent@example.com"), Owner = U("owner@example.com"), GroupAdmin = U("admin@example.com"),
            Pending = U("pending@example.com"), Stranger = U("stranger@example.com"),
            Swimmer = new Swimmer { LastName = "ברנצב", FirstName = "סבינה", BirthYear = 2014 },
            Comp = new Competition { Name = "Meet", Date = "16/07/2026", PoolType = "25m" }
        };
        var style = new Style { Name = "Freestyle" };
        var club = new Club { Name = "הפועל דולפין נתניה" };
        db.AddRange(s.Publisher, s.Owner, s.GroupAdmin, s.Pending, s.Stranger, s.Swimmer, s.Comp, style, club);
        await db.SaveChangesAsync();

        var result = new ResultRecord
        {
            SwimmerId = s.Swimmer.Id, ClubId = club.Id, CompetitionId = s.Comp.Id, StyleId = style.Id,
            Distance = "50", Gender = "female", CompetitionDate = new DateTime(2026, 7, 16)
        };
        db.Results.Add(result);
        var group = new HubGroup { Name = "Dolphin parents", Slug = "dolphin-parents", OwnerUserId = s.Owner.Id };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();

        db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = group.Id, SwimmerId = s.Swimmer.Id });
        db.HubGroupUserMembers.AddRange(
            new HubGroupUserMember { HubGroupId = group.Id, UserId = s.Publisher.Id, Status = HubGroupUserMemberStatus.Active },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = s.Pending.Id, Status = HubGroupUserMemberStatus.Pending });
        db.HubGroupAdmins.Add(new HubGroupAdmin { HubGroupId = group.Id, UserId = s.GroupAdmin.Id, GrantedByUserId = s.Owner.Id });
        s.Media = new UserMedia
        {
            UserId = s.Publisher.Id, SwimmerId = s.Swimmer.Id, ResultId = result.Id, CompetitionId = s.Comp.Id,
            Level = "result", MediaType = "video", SourceType = "youtube", Url = "https://www.youtube.com/watch?v=sabina50"
        };
        db.UserMedia.Add(s.Media);
        await db.SaveChangesAsync();

        var svc = new UserMediaPublicationService(db);
        var submit = await svc.SubmitAsync(s.Publisher.Id, s.Media.Id,
            new SubmitPublicationRequest { TargetType = UserMediaPublicationTarget.Group, TargetId = group.Id, Level = "members" },
            isPrivileged: false);
        Assert.True(submit.Success, submit.Error);
        Assert.True(await svc.DecideAsync(UserMediaPublicationTarget.Group, group.Id, submit.Publication!.Id, approve: true, decidedByUserId: s.Owner.Id));
        return s;
    }

    private static async Task<bool> SeesInProtocolAsync(SwimmDbContext db, Seed s, int? userId, bool isSiteAdmin = false) =>
        (await new UserMediaPublicationService(db).GetVisibleForResultsAsync(s.Comp.Id, null, null, userId, isSiteAdmin))
            .Any(v => v.Url == s.Media.Url);

    private static async Task<bool> SeesOnSwimmerPageAsync(SwimmDbContext db, Seed s, int? userId, bool isSiteAdmin = false) =>
        (await new UserMediaPublicationService(db).GetVisibleForSwimmerAsync(s.Swimmer.Id, userId, isSiteAdmin))
            .Any(v => v.Url == s.Media.Url);

    [Fact]
    public async Task MembersVideo_VisibleToGroupManagers_NotOnlyToAccountMembers()
    {
        await using var db = CreateDb(nameof(MembersVideo_VisibleToGroupManagers_NotOnlyToAccountMembers));
        var s = await SeedAsync(db);

        // Участник-аккаунт видел и раньше.
        Assert.True(await SeesInProtocolAsync(db, s, s.Publisher.Id));
        // Владелец и админ группы, не вступившие участниками, — теперь видят и в протоколе,
        // и на странице пловца: как на странице группы.
        Assert.True(await SeesInProtocolAsync(db, s, s.Owner.Id));
        Assert.True(await SeesOnSwimmerPageAsync(db, s, s.Owner.Id));
        Assert.True(await SeesInProtocolAsync(db, s, s.GroupAdmin.Id));
        Assert.True(await SeesOnSwimmerPageAsync(db, s, s.GroupAdmin.Id));
        // Админ сайта — как на странице группы (CanEdit).
        Assert.True(await SeesInProtocolAsync(db, s, s.Stranger.Id, isSiteAdmin: true));
    }

    [Fact]
    public async Task MembersVideo_HiddenFromPendingStrangerAndGuest()
    {
        await using var db = CreateDb(nameof(MembersVideo_HiddenFromPendingStrangerAndGuest));
        var s = await SeedAsync(db);

        Assert.False(await SeesInProtocolAsync(db, s, s.Pending.Id));   // заявка не одобрена
        Assert.False(await SeesOnSwimmerPageAsync(db, s, s.Pending.Id));
        Assert.False(await SeesInProtocolAsync(db, s, s.Stranger.Id));
        Assert.False(await SeesInProtocolAsync(db, s, null));
        Assert.False(await SeesOnSwimmerPageAsync(db, s, null));
    }

    [Fact]
    public async Task Like_FollowsSameAudience()
    {
        await using var db = CreateDb(nameof(Like_FollowsSameAudience));
        var s = await SeedAsync(db);
        var repo = new ReactionRepository(db);

        Assert.NotNull(await repo.SetLikeAsync(s.GroupAdmin.Id, s.Media.Id, on: true, isSiteAdmin: false));
        Assert.NotNull(await repo.SetLikeAsync(s.Owner.Id, s.Media.Id, on: true, isSiteAdmin: false));
        // Раньше проходил: копия правила в лайке не смотрела на статус заявки.
        Assert.Null(await repo.SetLikeAsync(s.Pending.Id, s.Media.Id, on: true, isSiteAdmin: false));
        Assert.Null(await repo.SetLikeAsync(s.Stranger.Id, s.Media.Id, on: true, isSiteAdmin: false));
    }
}
