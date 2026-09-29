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
        public HubGroup Group = null!;
    }

    /// <summary>
    /// Родитель-участник публикует видео заплыва в группу уровнем members; владелец одобряет.
    /// Владелец и админ группы — НЕ участники-аккаунты (так и бывает у тренера-админа).
    /// </summary>
    /// <param name="level">Уровень публикации: members (по умолчанию) или public.</param>
    /// <param name="configureGroup">Флаги группы до сохранения — IsTrusted / IsOfficial (Р56).</param>
    private static async Task<Seed> SeedAsync(SwimmDbContext db, string level = "members", Action<HubGroup, Club>? configureGroup = null)
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
        configureGroup?.Invoke(group, club);
        s.Group = group;
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
            new SubmitPublicationRequest { TargetType = UserMediaPublicationTarget.Group, TargetId = group.Id, Level = level },
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

    /// <summary>Страница результатов группы (<c>/groups/{slug}/results</c>) — её собственная страница.</summary>
    private static async Task<bool> SeesOnGroupResultsAsync(SwimmDbContext db, Seed s, int? userId, string? slug = null) =>
        (await new UserMediaPublicationService(db).GetVisibleForResultsAsync(null, null, slug ?? s.Group.Slug, userId, false))
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

    // ── Р56/Р65: «группа только следит; публичное — только Trusted» (И15) ─────────────────────

    /// <summary>Public, одобренный пока группа была доверенной, — и флаг потом сняли.</summary>
    private static async Task<Seed> SeedUntrustedPublicAsync(SwimmDbContext db)
    {
        var s = await SeedAsync(db, "public", (g, _) => g.IsTrusted = true);
        s.Group.IsTrusted = false;
        await db.SaveChangesAsync();
        return s;
    }

    [Fact]
    public async Task Submit_EveryoneToUntrustedGroup_Refused()
    {
        await using var db = CreateDb(nameof(Submit_EveryoneToUntrustedGroup_Refused));
        var s = await SeedAsync(db);   // members-публикация в обычную группу проходит

        var again = await new UserMediaPublicationService(db).SubmitAsync(s.Publisher.Id, s.Media.Id,
            new SubmitPublicationRequest { TargetType = UserMediaPublicationTarget.Group, TargetId = s.Group.Id, Level = "public" },
            isPrivileged: true);
        Assert.False(again.Success);
        Assert.Contains("Trusted", again.Error);
    }

    [Fact]
    public async Task PublicVideo_TrustRemoved_ActsAsMembersEverywhere_IncludingGroupPage()
    {
        await using var db = CreateDb(nameof(PublicVideo_TrustRemoved_ActsAsMembersEverywhere_IncludingGroupPage));
        var s = await SeedUntrustedPublicAsync(db);
        var svc = new UserMediaPublicationService(db);

        // Гостю и постороннему — нигде, и на странице самой группы тоже (середины больше нет).
        Assert.False(await SeesInProtocolAsync(db, s, null));
        Assert.False(await SeesOnSwimmerPageAsync(db, s, s.Stranger.Id));
        Assert.False(await SeesOnGroupResultsAsync(db, s, null));
        Assert.False(await SeesOnGroupResultsAsync(db, s, s.Stranger.Id));
        Assert.DoesNotContain(await svc.GetApprovedForGroupAsync(s.Group.Id, "public"), v => v.MediaId == s.Media.Id);
        // Участникам и управляющим — как members, и в ленте участников группы.
        Assert.True(await SeesInProtocolAsync(db, s, s.Publisher.Id));
        Assert.True(await SeesOnSwimmerPageAsync(db, s, s.Owner.Id));
        Assert.True(await SeesOnGroupResultsAsync(db, s, s.GroupAdmin.Id));
        Assert.Contains(await svc.GetApprovedForGroupAsync(s.Group.Id, "members"), v => v.MediaId == s.Media.Id);
        Assert.True(await SeesInProtocolAsync(db, s, s.Stranger.Id, isSiteAdmin: true));
    }

    [Fact]
    public async Task PublicVideo_TrustedGroup_VisibleToAllEverywhere()
    {
        await using var db = CreateDb(nameof(PublicVideo_TrustedGroup_VisibleToAllEverywhere));
        var s = await SeedAsync(db, "public", (g, _) => g.IsTrusted = true);

        Assert.True(await SeesInProtocolAsync(db, s, null));
        Assert.True(await SeesOnSwimmerPageAsync(db, s, null));
        Assert.True(await SeesOnSwimmerPageAsync(db, s, s.Stranger.Id));
        Assert.True(await SeesOnGroupResultsAsync(db, s, null));
        var svc = new UserMediaPublicationService(db);
        Assert.Contains(await svc.GetApprovedForGroupAsync(s.Group.Id, "public"), v => v.MediaId == s.Media.Id);
        // В ленте участников доверенной группы public не дублируется.
        Assert.DoesNotContain(await svc.GetApprovedForGroupAsync(s.Group.Id, "members"), v => v.MediaId == s.Media.Id);
    }

    [Fact]
    public async Task PublicVideo_OfficialClubGroup_TrustedWithoutFlag()
    {
        await using var db = CreateDb(nameof(PublicVideo_OfficialClubGroup_TrustedWithoutFlag));
        var s = await SeedAsync(db, "public", (g, club) => { g.IsOfficial = true; g.ClubId = club.Id; });

        Assert.True(await SeesInProtocolAsync(db, s, null));
        Assert.True(await SeesOnSwimmerPageAsync(db, s, s.Stranger.Id));
    }

    [Fact]
    public async Task PublicVideo_TrustDoesNotOpenMembersLevel()
    {
        await using var db = CreateDb(nameof(PublicVideo_TrustDoesNotOpenMembersLevel));
        var s = await SeedAsync(db, "members", (g, _) => g.IsTrusted = true);

        // «Trusted» — право делать публичным, а не пересмотр уровня: members остаётся группе.
        Assert.False(await SeesInProtocolAsync(db, s, null));
        Assert.False(await SeesOnSwimmerPageAsync(db, s, s.Stranger.Id));
    }

    [Fact]
    public async Task PublicVideo_TrustRemoved_SwimmerAccountLinkDoesNotReopenIt()
    {
        await using var db = CreateDb(nameof(PublicVideo_TrustRemoved_SwimmerAccountLinkDoesNotReopenIt));
        var s = await SeedUntrustedPublicAsync(db);

        // Р61 снят Р65: привязка аккаунта к пловцу публичным не делает — публичное только Trusted.
        s.Publisher.SwimmerId = s.Swimmer.Id;
        await db.SaveChangesAsync();
        Assert.False(await SeesOnSwimmerPageAsync(db, s, null));
    }

    [Fact]
    public async Task Like_FollowsVisibility_UntrustedPublicNotForStrangers()
    {
        await using var db = CreateDb(nameof(Like_FollowsVisibility_UntrustedPublicNotForStrangers));
        var s = await SeedUntrustedPublicAsync(db);
        var repo = new ReactionRepository(db);

        Assert.Null(await repo.SetLikeAsync(s.Stranger.Id, s.Media.Id, on: true, isSiteAdmin: false));
        Assert.NotNull(await repo.SetLikeAsync(s.Owner.Id, s.Media.Id, on: true, isSiteAdmin: false));
    }
}
