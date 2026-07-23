using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты <see cref="UserMediaPublicationService"/> (этап 2 media-visibility-model, память
/// media-visibility-model): подача личного медиа в группы, модерация, изоляция между группами.
/// EF InMemory — по образцу <see cref="HubGroupMediaServiceTests"/>/<see cref="UserMediaRepositoryTests"/>.
/// </summary>
public class UserMediaPublicationServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static AppUser NewUser(string email) =>
        new() { Email = email, DisplayName = email, SecurityStamp = Guid.NewGuid().ToString("N") };

    private static Swimmer NewSwimmer(string last, string first) =>
        new() { LastName = last, FirstName = first, LastNameEn = last, FirstNameEn = first, BirthYear = 2010 };

    private static UserMedia NewMedia(AppUser owner, Swimmer swimmer) => new()
    {
        UserId = owner.Id,
        SwimmerId = swimmer.Id,
        Level = "swimmer",
        MediaType = "video",
        SourceType = "youtube",
        Url = "https://www.youtube.com/watch?v=abc123",
        Visibility = "private",
    };

    // ── Набор 1 — сценарный тест (дословно сценарий Влада) ──────────────────

    [Fact]
    public async Task Scenario_VladFamily_PublicationsAcrossThreeGroups()
    {
        await using var db = CreateDb(nameof(Scenario_VladFamily_PublicationsAcrossThreeGroups));

        // ── Сидинг: юзеры, пловцы, группы, ростеры, членство ──
        var vlad = NewUser("vlad@example.com");
        var stranger = NewUser("stranger@example.com");
        var coach = NewUser("coach@example.com");
        db.AppUsers.AddRange(vlad, stranger, coach);
        await db.SaveChangesAsync();

        var vladSwimmer = NewSwimmer("Барцев", "Владимир");
        var child1 = NewSwimmer("Барцев", "Реб1");
        var child2 = NewSwimmer("Барцев", "Реб2");
        db.Swimmers.AddRange(vladSwimmer, child1, child2);
        await db.SaveChangesAsync();

        var g1 = new HubGroup { Name = "Мастерс", Slug = "masters", OwnerUserId = coach.Id, IsPublic = true };
        var g2 = new HubGroup { Name = "G2", Slug = "g2", OwnerUserId = coach.Id, IsPublic = true };
        var g3 = new HubGroup { Name = "G3", Slug = "g3", OwnerUserId = coach.Id, IsPublic = true };
        db.HubGroups.AddRange(g1, g2, g3);
        await db.SaveChangesAsync();

        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = g1.Id, SwimmerId = vladSwimmer.Id },
            new HubGroupMember { HubGroupId = g2.Id, SwimmerId = child1.Id },
            new HubGroupMember { HubGroupId = g3.Id, SwimmerId = child2.Id });

        db.HubGroupUserMembers.AddRange(
            new HubGroupUserMember { HubGroupId = g1.Id, UserId = vlad.Id, Status = HubGroupUserMemberStatus.Active },
            new HubGroupUserMember { HubGroupId = g2.Id, UserId = vlad.Id, Status = HubGroupUserMemberStatus.Active },
            new HubGroupUserMember { HubGroupId = g3.Id, UserId = vlad.Id, Status = HubGroupUserMemberStatus.Active },
            new HubGroupUserMember { HubGroupId = g1.Id, UserId = stranger.Id, Status = HubGroupUserMemberStatus.Active });
        await db.SaveChangesAsync();

        // 1. Медиа
        var mSelf = NewMedia(vlad, vladSwimmer);
        var mC1 = NewMedia(vlad, child1);
        var mC2 = NewMedia(vlad, child2);
        db.UserMedia.AddRange(mSelf, mC1, mC2);
        await db.SaveChangesAsync();

        var service = new UserMediaPublicationService(db);

        // 2. M_self остаётся приватным — публикаций ещё не подавали, все inbox-ы пусты
        Assert.Empty(await service.GetForGroupAsync(g1.Id));
        Assert.Empty(await service.GetForGroupAsync(g2.Id));
        Assert.Empty(await service.GetForGroupAsync(g3.Id));

        // 3. Влад подаёт M_c1 в G2 (members)
        var submitC1G2 = await service.SubmitAsync(vlad.Id, mC1.Id, new SubmitPublicationRequest { HubGroupId = g2.Id, Level = "members" }, isGroupPrivileged: false);
        Assert.True(submitC1G2.Success);
        Assert.Equal(UserMediaPublicationStatus.Pending, submitC1G2.Publication!.Status);
        Assert.Empty(await service.GetApprovedForGroupAsync(g2.Id, "members"));
        Assert.Single(await service.GetForGroupAsync(g2.Id));
        Assert.Contains(await service.GetForOwnerAsync(vlad.Id), p => p.Id == submitC1G2.Publication.Id && p.Status == UserMediaPublicationStatus.Pending);

        // 4. Влад подаёт M_self в G1 (members) → Coach отклоняет
        var submitSelfG1 = await service.SubmitAsync(vlad.Id, mSelf.Id, new SubmitPublicationRequest { HubGroupId = g1.Id, Level = "members" }, isGroupPrivileged: false);
        Assert.True(submitSelfG1.Success);
        Assert.Equal(UserMediaPublicationStatus.Pending, submitSelfG1.Publication!.Status);

        var decideRejectSelf = await service.DecideAsync(g1.Id, submitSelfG1.Publication.Id, approve: false, decidedByUserId: coach.Id);
        Assert.True(decideRejectSelf);
        Assert.DoesNotContain(await service.GetForGroupAsync(g1.Id), p => p.Id == submitSelfG1.Publication.Id);
        var ownerAfterReject = await service.GetForOwnerAsync(vlad.Id);
        Assert.Contains(ownerAfterReject, p => p.Id == submitSelfG1.Publication.Id && p.Status == UserMediaPublicationStatus.Rejected);

        // 5. Coach одобряет заявку M_c1 → G2
        var decideApproveC1 = await service.DecideAsync(g2.Id, submitC1G2.Publication.Id, approve: true, decidedByUserId: coach.Id);
        Assert.True(decideApproveC1);
        var approvedG2Members = await service.GetApprovedForGroupAsync(g2.Id, "members");
        var approvedC1 = Assert.Single(approvedG2Members);
        Assert.Equal(submitC1G2.Publication.Id, approvedC1.Id);
        Assert.Contains("Реб1", approvedC1.SwimmerName);

        // 6. Влад подаёт M_c2 в G3 (public), Coach одобряет
        var submitC2G3 = await service.SubmitAsync(vlad.Id, mC2.Id, new SubmitPublicationRequest { HubGroupId = g3.Id, Level = "public" }, isGroupPrivileged: false);
        Assert.True(submitC2G3.Success);
        var decideApproveC2 = await service.DecideAsync(g3.Id, submitC2G3.Publication!.Id, approve: true, decidedByUserId: coach.Id);
        Assert.True(decideApproveC2);
        Assert.Single(await service.GetApprovedForGroupAsync(g3.Id, "public"));
        Assert.Empty(await service.GetApprovedForGroupAsync(g3.Id, "members"));

        // 7. Кросс-проверки изоляции
        Assert.DoesNotContain(await service.GetApprovedForGroupAsync(g1.Id, "members"), p => p.SwimmerId == child1.Id);
        Assert.DoesNotContain(await service.GetApprovedForGroupAsync(g3.Id, "public"), p => p.SwimmerId == child1.Id);
        Assert.DoesNotContain(await service.GetApprovedForGroupAsync(g1.Id, "members"), p => p.SwimmerId == child2.Id);
        Assert.DoesNotContain(await service.GetApprovedForGroupAsync(g2.Id, "members"), p => p.SwimmerId == child2.Id);

        var submitC1G1 = await service.SubmitAsync(vlad.Id, mC1.Id, new SubmitPublicationRequest { HubGroupId = g1.Id, Level = "members" }, isGroupPrivileged: false);
        Assert.False(submitC1G1.Success);
        Assert.Equal("swimmer is not in this group's roster", submitC1G1.Error);

        // 8. Каскад: удаление M_c2 → её публикация исчезает из G3
        var trackedC2 = await db.UserMedia.FindAsync(mC2.Id);
        db.UserMedia.Remove(trackedC2!);
        await db.SaveChangesAsync();

        Assert.Empty(await service.GetApprovedForGroupAsync(g3.Id, "public"));
    }

    // ── Набор 2 — матрица правил ─────────────────────────────────────────────

    private static async Task<(AppUser owner, Swimmer swimmer, HubGroup group, UserMedia media)> SeedBasicAsync(SwimmDbContext db, bool ownerIsActiveMember = true)
    {
        var owner = NewUser("owner@example.com");
        db.AppUsers.Add(owner);
        await db.SaveChangesAsync();

        var swimmer = NewSwimmer("Иванов", "Иван");
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();

        var group = new HubGroup { Name = "G", Slug = Guid.NewGuid().ToString("N"), OwnerUserId = owner.Id, IsPublic = true };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();

        db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = group.Id, SwimmerId = swimmer.Id });
        if (ownerIsActiveMember)
            db.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = group.Id, UserId = owner.Id, Status = HubGroupUserMemberStatus.Active });
        await db.SaveChangesAsync();

        var media = NewMedia(owner, swimmer);
        db.UserMedia.Add(media);
        await db.SaveChangesAsync();

        return (owner, swimmer, group, media);
    }

    [Theory]
    [InlineData("friends")]
    [InlineData("")]
    public async Task SubmitAsync_InvalidLevel_Rejected(string level)
    {
        await using var db = CreateDb(nameof(SubmitAsync_InvalidLevel_Rejected) + level.Length + level.GetHashCode());
        var (owner, _, group, media) = await SeedBasicAsync(db);
        var service = new UserMediaPublicationService(db);

        var result = await service.SubmitAsync(owner.Id, media.Id, new SubmitPublicationRequest { HubGroupId = group.Id, Level = level }, isGroupPrivileged: false);

        Assert.False(result.Success);
        Assert.Equal("level must be 'members' or 'public'", result.Error);
    }

    [Fact]
    public async Task SubmitAsync_ForeignMedia_MediaNotFound()
    {
        await using var db = CreateDb(nameof(SubmitAsync_ForeignMedia_MediaNotFound));
        var (owner, _, group, media) = await SeedBasicAsync(db);
        var stranger = NewUser("stranger@example.com");
        db.AppUsers.Add(stranger);
        await db.SaveChangesAsync();
        var service = new UserMediaPublicationService(db);

        var result = await service.SubmitAsync(stranger.Id, media.Id, new SubmitPublicationRequest { HubGroupId = group.Id, Level = "members" }, isGroupPrivileged: false);

        Assert.False(result.Success);
        Assert.Equal("media not found", result.Error);
    }

    [Fact]
    public async Task SubmitAsync_UnknownGroup_GroupNotFound()
    {
        await using var db = CreateDb(nameof(SubmitAsync_UnknownGroup_GroupNotFound));
        var (owner, _, _, media) = await SeedBasicAsync(db);
        var service = new UserMediaPublicationService(db);

        var result = await service.SubmitAsync(owner.Id, media.Id, new SubmitPublicationRequest { HubGroupId = 999999, Level = "members" }, isGroupPrivileged: false);

        Assert.False(result.Success);
        Assert.Equal("group not found", result.Error);
    }

    [Fact]
    public async Task SubmitAsync_NotActiveMember_Rejected()
    {
        await using var db = CreateDb(nameof(SubmitAsync_NotActiveMember_Rejected));
        var (owner, _, group, media) = await SeedBasicAsync(db, ownerIsActiveMember: false);
        var service = new UserMediaPublicationService(db);

        var result = await service.SubmitAsync(owner.Id, media.Id, new SubmitPublicationRequest { HubGroupId = group.Id, Level = "members" }, isGroupPrivileged: false);

        Assert.False(result.Success);
        Assert.Equal("you are not an active member of this group", result.Error);
    }

    [Fact]
    public async Task SubmitAsync_PendingMember_Rejected()
    {
        await using var db = CreateDb(nameof(SubmitAsync_PendingMember_Rejected));
        var (owner, _, group, media) = await SeedBasicAsync(db, ownerIsActiveMember: false);
        db.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = group.Id, UserId = owner.Id, Status = HubGroupUserMemberStatus.Pending });
        await db.SaveChangesAsync();
        var service = new UserMediaPublicationService(db);

        var result = await service.SubmitAsync(owner.Id, media.Id, new SubmitPublicationRequest { HubGroupId = group.Id, Level = "members" }, isGroupPrivileged: false);

        Assert.False(result.Success);
        Assert.Equal("you are not an active member of this group", result.Error);
    }

    [Fact]
    public async Task SubmitAsync_Privileged_SkipsMembershipAndApprovesImmediately()
    {
        await using var db = CreateDb(nameof(SubmitAsync_Privileged_SkipsMembershipAndApprovesImmediately));
        var (owner, _, group, media) = await SeedBasicAsync(db, ownerIsActiveMember: false);
        var service = new UserMediaPublicationService(db);

        var result = await service.SubmitAsync(owner.Id, media.Id, new SubmitPublicationRequest { HubGroupId = group.Id, Level = "members" }, isGroupPrivileged: true);

        Assert.True(result.Success);
        Assert.Equal(UserMediaPublicationStatus.Approved, result.Publication!.Status);
        var stored = await db.UserMediaPublications.AsNoTracking().FirstAsync(p => p.Id == result.Publication.Id);
        Assert.Equal(owner.Id, stored.DecidedByUserId);
        Assert.NotNull(stored.DecidedAt);
    }

    [Fact]
    public async Task SubmitAsync_DuplicateWhilePending_AlreadyExists()
    {
        await using var db = CreateDb(nameof(SubmitAsync_DuplicateWhilePending_AlreadyExists));
        var (owner, _, group, media) = await SeedBasicAsync(db);
        var service = new UserMediaPublicationService(db);
        await service.SubmitAsync(owner.Id, media.Id, new SubmitPublicationRequest { HubGroupId = group.Id, Level = "members" }, isGroupPrivileged: false);

        var result = await service.SubmitAsync(owner.Id, media.Id, new SubmitPublicationRequest { HubGroupId = group.Id, Level = "public" }, isGroupPrivileged: false);

        Assert.False(result.Success);
        Assert.Equal("publication already exists", result.Error);
    }

    [Fact]
    public async Task SubmitAsync_DuplicateWhileApproved_AlreadyExists()
    {
        await using var db = CreateDb(nameof(SubmitAsync_DuplicateWhileApproved_AlreadyExists));
        var (owner, _, group, media) = await SeedBasicAsync(db);
        var service = new UserMediaPublicationService(db);
        var first = await service.SubmitAsync(owner.Id, media.Id, new SubmitPublicationRequest { HubGroupId = group.Id, Level = "members" }, isGroupPrivileged: false);
        await service.DecideAsync(group.Id, first.Publication!.Id, approve: true, decidedByUserId: owner.Id);

        var result = await service.SubmitAsync(owner.Id, media.Id, new SubmitPublicationRequest { HubGroupId = group.Id, Level = "public" }, isGroupPrivileged: false);

        Assert.False(result.Success);
        Assert.Equal("publication already exists", result.Error);
    }

    [Fact]
    public async Task SubmitAsync_ResubmitAfterRejected_SameRowBackToPendingWithNewLevel()
    {
        await using var db = CreateDb(nameof(SubmitAsync_ResubmitAfterRejected_SameRowBackToPendingWithNewLevel));
        var (owner, _, group, media) = await SeedBasicAsync(db);
        var service = new UserMediaPublicationService(db);
        var first = await service.SubmitAsync(owner.Id, media.Id, new SubmitPublicationRequest { HubGroupId = group.Id, Level = "members" }, isGroupPrivileged: false);
        await service.DecideAsync(group.Id, first.Publication!.Id, approve: false, decidedByUserId: owner.Id);

        var resubmit = await service.SubmitAsync(owner.Id, media.Id, new SubmitPublicationRequest { HubGroupId = group.Id, Level = "public" }, isGroupPrivileged: false);

        Assert.True(resubmit.Success);
        Assert.Equal(first.Publication.Id, resubmit.Publication!.Id);
        Assert.Equal(UserMediaPublicationStatus.Pending, resubmit.Publication.Status);
        Assert.Equal("public", resubmit.Publication.Level);
        var stored = await db.UserMediaPublications.AsNoTracking().FirstAsync(p => p.Id == first.Publication.Id);
        Assert.Null(stored.DecidedByUserId);
        Assert.Null(stored.DecidedAt);
    }

    [Fact]
    public async Task WithdrawAsync_WrongOwner_ReturnsFalseAndKeepsRow()
    {
        await using var db = CreateDb(nameof(WithdrawAsync_WrongOwner_ReturnsFalseAndKeepsRow));
        var (owner, _, group, media) = await SeedBasicAsync(db);
        var stranger = NewUser("stranger2@example.com");
        db.AppUsers.Add(stranger);
        await db.SaveChangesAsync();
        var service = new UserMediaPublicationService(db);
        var submitted = await service.SubmitAsync(owner.Id, media.Id, new SubmitPublicationRequest { HubGroupId = group.Id, Level = "members" }, isGroupPrivileged: false);

        var result = await service.WithdrawAsync(stranger.Id, media.Id, group.Id);

        Assert.False(result);
        Assert.NotNull(await db.UserMediaPublications.FindAsync(submitted.Publication!.Id));
    }

    [Fact]
    public async Task WithdrawAsync_Owner_RemovesRow()
    {
        await using var db = CreateDb(nameof(WithdrawAsync_Owner_RemovesRow));
        var (owner, _, group, media) = await SeedBasicAsync(db);
        var service = new UserMediaPublicationService(db);
        var submitted = await service.SubmitAsync(owner.Id, media.Id, new SubmitPublicationRequest { HubGroupId = group.Id, Level = "members" }, isGroupPrivileged: false);

        var result = await service.WithdrawAsync(owner.Id, media.Id, group.Id);

        Assert.True(result);
        Assert.Null(await db.UserMediaPublications.FindAsync(submitted.Publication!.Id));
    }

    [Fact]
    public async Task DecideAsync_WrongGroupId_ReturnsFalse()
    {
        await using var db = CreateDb(nameof(DecideAsync_WrongGroupId_ReturnsFalse));
        var (owner, _, group, media) = await SeedBasicAsync(db);
        var service = new UserMediaPublicationService(db);
        var submitted = await service.SubmitAsync(owner.Id, media.Id, new SubmitPublicationRequest { HubGroupId = group.Id, Level = "members" }, isGroupPrivileged: false);

        var result = await service.DecideAsync(group.Id + 999, submitted.Publication!.Id, approve: true, decidedByUserId: owner.Id);

        Assert.False(result);
    }

    [Fact]
    public async Task DecideAsync_ApprovedThenRejected_WithdrawsFromPublication()
    {
        await using var db = CreateDb(nameof(DecideAsync_ApprovedThenRejected_WithdrawsFromPublication));
        var (owner, _, group, media) = await SeedBasicAsync(db);
        var service = new UserMediaPublicationService(db);
        var submitted = await service.SubmitAsync(owner.Id, media.Id, new SubmitPublicationRequest { HubGroupId = group.Id, Level = "members" }, isGroupPrivileged: false);
        await service.DecideAsync(group.Id, submitted.Publication!.Id, approve: true, decidedByUserId: owner.Id);
        Assert.Single(await service.GetApprovedForGroupAsync(group.Id, "members"));

        var result = await service.DecideAsync(group.Id, submitted.Publication.Id, approve: false, decidedByUserId: owner.Id);

        Assert.True(result);
        Assert.Empty(await service.GetApprovedForGroupAsync(group.Id, "members"));
    }

    // ── GetPublishTargetsAsync — честный селектор «куда можно подать» ────────

    [Fact]
    public async Task GetPublishTargets_RosterAndMembership_ReturnsGroup()
    {
        await using var db = CreateDb(nameof(GetPublishTargets_RosterAndMembership_ReturnsGroup));
        var (owner, _, group, media) = await SeedBasicAsync(db);
        var service = new UserMediaPublicationService(db);

        var targets = await service.GetPublishTargetsAsync(owner.Id, media.Id);

        Assert.Equal(group.Id, Assert.Single(targets).Id);
    }

    [Fact]
    public async Task GetPublishTargets_SwimmerNotInRoster_GroupExcluded()
    {
        await using var db = CreateDb(nameof(GetPublishTargets_SwimmerNotInRoster_GroupExcluded));
        var (owner, _, _, media) = await SeedBasicAsync(db);
        // Вторая группа: владелец — член, но пловца медиа в ростере нет (кейс «Дельфин мастерс
        // для видео Сабины») — предлагаться не должна.
        var other = new HubGroup { Name = "Other", Slug = Guid.NewGuid().ToString("N"), OwnerUserId = owner.Id, IsPublic = true };
        db.HubGroups.Add(other);
        await db.SaveChangesAsync();
        db.HubGroupUserMembers.Add(new HubGroupUserMember { HubGroupId = other.Id, UserId = owner.Id, Status = HubGroupUserMemberStatus.Active });
        await db.SaveChangesAsync();
        var service = new UserMediaPublicationService(db);

        var targets = await service.GetPublishTargetsAsync(owner.Id, media.Id);

        Assert.DoesNotContain(targets, t => t.Id == other.Id);
    }

    [Fact]
    public async Task GetPublishTargets_OwnerWithoutMembership_StillIncluded()
    {
        // Владелец/админ группы подаёт без user-членства (isGroupPrivileged) — селектор
        // обязан предлагать такую группу.
        await using var db = CreateDb(nameof(GetPublishTargets_OwnerWithoutMembership_StillIncluded));
        var (owner, _, group, media) = await SeedBasicAsync(db, ownerIsActiveMember: false);
        var service = new UserMediaPublicationService(db);

        var targets = await service.GetPublishTargetsAsync(owner.Id, media.Id);

        Assert.Equal(group.Id, Assert.Single(targets).Id);
    }

    [Fact]
    public async Task GetPublishTargets_ForeignMedia_Empty()
    {
        await using var db = CreateDb(nameof(GetPublishTargets_ForeignMedia_Empty));
        var (_, _, _, media) = await SeedBasicAsync(db);
        var stranger = NewUser("stranger2@example.com");
        db.AppUsers.Add(stranger);
        await db.SaveChangesAsync();
        var service = new UserMediaPublicationService(db);

        Assert.Empty(await service.GetPublishTargetsAsync(stranger.Id, media.Id));
    }
}
