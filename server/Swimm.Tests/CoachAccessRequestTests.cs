using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Заявка тренера на группу (docs/plans/coach-access-request-plan.md): «Request access» вступает
/// с пометкой, одобрение даёт админа + active + метку на «Me» (Т1), отказ снимает только пометку,
/// push — только владельцу и только на новую заявку (Т3). Запись в настоящий Postgres —
/// <see cref="CoachAccessRequestPgTests"/>.
/// </summary>
public class CoachAccessRequestTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class SettingsStub : ISettingsService
    {
        public IReadOnlyList<AdminSetting> GetAll() => [];
        public AdminSetting? Get(string key) => null;
        public T GetValue<T>(string key, T fallback) => fallback;
        public bool Update(string key, string newValue) => true;
    }

    private static HubGroupUserService Service(SwimmDbContext db) =>
        new(db, new HubGroupCrudCore(db), new SettingsStub());

    private sealed record World(HubGroup Group, AppUser Owner, AppUser Coach, Swimmer CoachSwimmer);

    /// <summary>Группа владельца, тренер с «Me»-пловцом; <paramref name="inRoster"/> — пловец в составе.</summary>
    private static async Task<World> SeedAsync(SwimmDbContext db, string joinPolicy = HubGroupJoinPolicy.Open, bool inRoster = true)
    {
        var owner = new AppUser { Email = "owner@example.com", DisplayName = "Owner", SecurityStamp = "s" };
        var coach = new AppUser { Email = "coach@example.com", DisplayName = "Anya", SecurityStamp = "s" };
        db.AppUsers.AddRange(owner, coach);
        var swimmer = new Swimmer { LastName = "גוסטומלסקי", FirstName = "אניה", LastNameEn = "Gostomelsky", FirstNameEn = "Anya", BirthYear = 1980 };
        db.Swimmers.Add(swimmer);
        await db.SaveChangesAsync();

        var group = new HubGroup { Name = "Dolphin", Slug = "dolphin", OwnerUserId = owner.Id, IsPublic = true, JoinPolicy = joinPolicy };
        db.HubGroups.Add(group);
        await db.SaveChangesAsync();

        db.UserFavorites.Add(new UserFavorite { UserId = coach.Id, TargetType = "swimmer", SwimmerId = swimmer.Id, IsPrimary = true });
        if (inRoster) db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = group.Id, SwimmerId = swimmer.Id, Role = "coach" });
        await db.SaveChangesAsync();
        return new World(group, owner, coach, swimmer);
    }

    [Fact]
    public async Task Request_NotMember_JoinsWithMark_AndIsListed()
    {
        await using var db = CreateDb(nameof(Request_NotMember_JoinsWithMark_AndIsListed));
        var w = await SeedAsync(db);
        var svc = Service(db);

        var result = await svc.RequestCoachAccessAsync(w.Group.Id, w.Coach.Id);

        Assert.True(result.Success, result.Error);
        Assert.True(result.Created);
        var row = await db.HubGroupUserMembers.SingleAsync();
        Assert.Equal(HubGroupUserMemberStatus.Active, row.Status);
        Assert.NotNull(row.CoachRequestedAt);

        var list = await svc.GetCoachRequestsAsync(w.Group.Id);
        var item = Assert.Single(list);
        Assert.Equal(w.Coach.Id, item.UserId);
        Assert.Equal(w.CoachSwimmer.Id, item.MeSwimmerId);
        Assert.Equal("גוסטומלסקי אניה", item.MeSwimmerName); // иврит по умолчанию
        Assert.True(item.MeSwimmerInGroup);
        Assert.True((await svc.GetJoinedAsync(w.Coach.Id)).Single().CoachRequested);
    }

    [Fact]
    public async Task Request_ExistingMember_MarksOnly_RepeatIsNotNew()
    {
        await using var db = CreateDb(nameof(Request_ExistingMember_MarksOnly_RepeatIsNotNew));
        var w = await SeedAsync(db);
        var svc = Service(db);
        Assert.True((await svc.JoinAsync(w.Group.Id, w.Coach.Id)).Success);

        var first = await svc.RequestCoachAccessAsync(w.Group.Id, w.Coach.Id);
        var second = await svc.RequestCoachAccessAsync(w.Group.Id, w.Coach.Id);

        Assert.True(first.Created);
        Assert.True(second.Success);
        Assert.False(second.Created); // второго push не будет
        Assert.Single(await db.HubGroupUserMembers.ToListAsync());
    }

    [Fact]
    public async Task Request_OwnerOrGroupAdmin_AlreadyCoach()
    {
        await using var db = CreateDb(nameof(Request_OwnerOrGroupAdmin_AlreadyCoach));
        var w = await SeedAsync(db);
        db.HubGroupAdmins.Add(new HubGroupAdmin { HubGroupId = w.Group.Id, UserId = w.Coach.Id, GrantedByUserId = w.Owner.Id });
        await db.SaveChangesAsync();
        var svc = Service(db);

        Assert.True((await svc.RequestCoachAccessAsync(w.Group.Id, w.Owner.Id)).AlreadyCoach);
        Assert.True((await svc.RequestCoachAccessAsync(w.Group.Id, w.Coach.Id)).AlreadyCoach);
        Assert.Empty(await db.HubGroupUserMembers.ToListAsync());
    }

    [Fact]
    public async Task Request_UnknownOrTestGroup_Fails()
    {
        await using var db = CreateDb(nameof(Request_UnknownOrTestGroup_Fails));
        var w = await SeedAsync(db);
        w.Group.IsTest = true; // не-тестовый аккаунт тестовой группы не видит
        await db.SaveChangesAsync();
        var svc = Service(db);

        Assert.False((await svc.RequestCoachAccessAsync(9999, w.Coach.Id)).Success);
        Assert.False((await svc.RequestCoachAccessAsync(w.Group.Id, w.Coach.Id)).Success);
    }

    [Fact]
    public async Task Approve_GrantsAdmin_ActivatesPending_LabelsMe()
    {
        await using var db = CreateDb(nameof(Approve_GrantsAdmin_ActivatesPending_LabelsMe));
        var w = await SeedAsync(db, HubGroupJoinPolicy.Approval);
        var svc = Service(db);
        await svc.RequestCoachAccessAsync(w.Group.Id, w.Coach.Id);
        Assert.Equal(HubGroupUserMemberStatus.Pending, (await db.HubGroupUserMembers.SingleAsync()).Status);

        var result = await svc.ApproveCoachRequestAsync(w.Group.Id, w.Coach.Id, w.Owner.Id);

        Assert.True(result.Success, result.Error);
        var admin = await db.HubGroupAdmins.SingleAsync();
        Assert.Equal(w.Coach.Id, admin.UserId);
        Assert.Equal(w.Owner.Id, admin.GrantedByUserId);
        var row = await db.HubGroupUserMembers.SingleAsync();
        Assert.Equal(HubGroupUserMemberStatus.Active, row.Status);
        Assert.Equal(w.CoachSwimmer.Id, row.SwimmerId);
        Assert.Null(row.CoachRequestedAt);
        Assert.Empty(await svc.GetCoachRequestsAsync(w.Group.Id));
    }

    [Fact]
    public async Task Approve_MeNotInRoster_NoLabel_ManualLabelKept()
    {
        await using var db = CreateDb(nameof(Approve_MeNotInRoster_NoLabel_ManualLabelKept));
        var w = await SeedAsync(db, inRoster: false);
        var svc = Service(db);
        await svc.RequestCoachAccessAsync(w.Group.Id, w.Coach.Id);
        Assert.False((await svc.GetCoachRequestsAsync(w.Group.Id)).Single().MeSwimmerInGroup);

        Assert.True((await svc.ApproveCoachRequestAsync(w.Group.Id, w.Coach.Id, w.Owner.Id)).Success);
        Assert.Null((await db.HubGroupUserMembers.SingleAsync()).SwimmerId);

        // Вторая группа: метку поставили руками — одобрение её не перебивает.
        var other = new Swimmer { LastName = "כהן", FirstName = "דנה", BirthYear = 2012 };
        db.Swimmers.Add(other);
        var g2 = new HubGroup { Name = "G2", Slug = "g2", OwnerUserId = w.Owner.Id, IsPublic = true };
        db.HubGroups.Add(g2);
        await db.SaveChangesAsync();
        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = g2.Id, SwimmerId = w.CoachSwimmer.Id },
            new HubGroupMember { HubGroupId = g2.Id, SwimmerId = other.Id });
        await db.SaveChangesAsync();
        await svc.RequestCoachAccessAsync(g2.Id, w.Coach.Id);
        await svc.SetUserMemberLabelAsync(g2.Id, w.Coach.Id, other.Id, "parent");

        Assert.True((await svc.ApproveCoachRequestAsync(g2.Id, w.Coach.Id, w.Owner.Id)).Success);
        Assert.Equal(other.Id, (await db.HubGroupUserMembers.SingleAsync(m => m.HubGroupId == g2.Id)).SwimmerId);
    }

    [Fact]
    public async Task Approve_AlreadyAdmin_NoDuplicateRow()
    {
        await using var db = CreateDb(nameof(Approve_AlreadyAdmin_NoDuplicateRow));
        var w = await SeedAsync(db);
        var svc = Service(db);
        await svc.RequestCoachAccessAsync(w.Group.Id, w.Coach.Id);
        // Пока заявка висела, владелец назначил его руками через «+ Admin».
        Assert.True((await svc.AddAdminAsync(w.Group.Id, w.Coach.Email, w.Owner.Id)).Success);

        Assert.True((await svc.ApproveCoachRequestAsync(w.Group.Id, w.Coach.Id, w.Owner.Id)).Success);
        Assert.Single(await db.HubGroupAdmins.ToListAsync());
    }

    [Fact]
    public async Task Decline_ClearsMark_KeepsMembership_CanRequestAgain()
    {
        await using var db = CreateDb(nameof(Decline_ClearsMark_KeepsMembership_CanRequestAgain));
        var w = await SeedAsync(db);
        var svc = Service(db);
        await svc.RequestCoachAccessAsync(w.Group.Id, w.Coach.Id);

        Assert.True((await svc.DeclineCoachRequestAsync(w.Group.Id, w.Coach.Id)).Success);

        var row = await db.HubGroupUserMembers.SingleAsync();
        Assert.Null(row.CoachRequestedAt);
        Assert.Equal(HubGroupUserMemberStatus.Active, row.Status);
        Assert.Empty(await db.HubGroupAdmins.ToListAsync());
        // Нет заявки — нечего ни одобрять, ни отклонять.
        Assert.False((await svc.ApproveCoachRequestAsync(w.Group.Id, w.Coach.Id, w.Owner.Id)).Success);
        Assert.False((await svc.DeclineCoachRequestAsync(w.Group.Id, w.Coach.Id)).Success);
        // Повторная заявка после отказа — можно (§2 плана).
        Assert.True((await svc.RequestCoachAccessAsync(w.Group.Id, w.Coach.Id)).Created);
    }

    // ── Push владельцу ───────────────────────────────────────────────────────

    private sealed class NotConfiguredSender : IPushSender
    {
        public bool IsConfigured => false;
        public string? PublicKey => null;
        public Task<PushDeliveryOutcome> SendAsync(PushTarget target, PushPayload payload, CancellationToken ct = default) =>
            throw new InvalidOperationException("must not send");
    }

    [Fact]
    public async Task Notify_SendsToOwnerDevicesOnly_DropsGone()
    {
        await using var db = CreateDb(nameof(Notify_SendsToOwnerDevicesOnly_DropsGone));
        var w = await SeedAsync(db);
        db.UserPushSubscriptions.AddRange(
            new UserPushSubscription { UserId = w.Owner.Id, Endpoint = "https://push.example/owner-phone", P256dh = "p", Auth = "a" },
            new UserPushSubscription { UserId = w.Owner.Id, Endpoint = "https://push.example/owner-gone", P256dh = "p", Auth = "a" },
            new UserPushSubscription { UserId = w.Coach.Id, Endpoint = "https://push.example/coach", P256dh = "p", Auth = "a" });
        await db.SaveChangesAsync();
        var sender = new PushSubscriptionServiceTests.FakeSender(e => e.EndsWith("gone") ? PushDeliveryOutcome.Gone : PushDeliveryOutcome.Delivered);

        var delivered = await new CoachRequestNotifier(db, sender, NullLogger<CoachRequestNotifier>.Instance)
            .NotifyOwnerAsync(w.Group.Id, w.Coach.Id);

        Assert.Equal(1, delivered);
        Assert.Equal(2, sender.Sent.Count);
        Assert.All(sender.Sent, s => Assert.DoesNotContain("coach", s.Target.Endpoint));
        var payload = sender.Sent[0].Payload;
        Assert.Equal("Coach request", payload.Title);
        Assert.Equal("Anya asks to coach Dolphin", payload.Body);
        Assert.Equal("/groups/dolphin?tab=admin", payload.Url);
        Assert.Equal($"coach-req:{w.Group.Id}", payload.Tag);
        Assert.False(await db.UserPushSubscriptions.AnyAsync(s => s.Endpoint.EndsWith("gone")));
    }

    [Fact]
    public async Task Notify_NotConfigured_Silent()
    {
        await using var db = CreateDb(nameof(Notify_NotConfigured_Silent));
        var w = await SeedAsync(db);
        db.UserPushSubscriptions.Add(new UserPushSubscription { UserId = w.Owner.Id, Endpoint = "https://push.example/x", P256dh = "p", Auth = "a" });
        await db.SaveChangesAsync();

        var delivered = await new CoachRequestNotifier(db, new NotConfiguredSender(), NullLogger<CoachRequestNotifier>.Instance)
            .NotifyOwnerAsync(w.Group.Id, w.Coach.Id);

        Assert.Equal(0, delivered);
    }
}
