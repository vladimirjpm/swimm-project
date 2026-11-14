using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Публикация плана дорожек (docs/plans/trainings-coach-flow-plan.md, CF3): решение 10 — идущий
/// пловец без уровня не на дорожке не даёт опубликовать — и push участникам «дорожки готовы».
/// </summary>
public class LanePlanPublishTests
{
    private static readonly DateOnly Day = new(2026, 9, 27);

    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    /// <summary>Группа: Levelled — с уровнем, NoLevel и Other — без; Member, Muted, Off — аккаунты-участники.</summary>
    private sealed record Seed(int GroupId, int Coach, int Levelled, int NoLevel, int Other, int LevelId,
        int Member, int Muted, int Off);

    private static async Task<Seed> SeedAsync(SwimmDbContext db)
    {
        AppUser User(string name) => new() { Email = $"{name}@example.com", DisplayName = name, SecurityStamp = "s" };
        var coach = User("coach");
        var member = User("member");
        var muted = User("muted");
        var off = User("off");
        db.AppUsers.AddRange(coach, member, muted, off);
        await db.SaveChangesAsync();

        var group = new HubGroup { Name = "Dolphin", Slug = "dolphin", OwnerUserId = coach.Id, IsPublic = true };
        var levelled = new Swimmer { LastName = "א", FirstName = "א", BirthYear = 1980 };
        var noLevel = new Swimmer { LastName = "", FirstName = "", LastNameEn = "Tom", BirthYear = 1981 };
        var other = new Swimmer { LastName = "ניב", FirstName = "", BirthYear = 1982 };
        db.AddRange(group, levelled, noLevel, other);
        await db.SaveChangesAsync();

        db.HubGroupMembers.AddRange(
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = levelled.Id, SortOrder = 1 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = noLevel.Id, SortOrder = 2 },
            new HubGroupMember { HubGroupId = group.Id, SwimmerId = other.Id, SortOrder = 3 });
        var level = new HubGroupLevel { HubGroupId = group.Id, Rank = 1, Name = "Fast" };
        db.HubGroupLevels.Add(level);
        db.HubGroupUserMembers.AddRange(
            new HubGroupUserMember { HubGroupId = group.Id, UserId = coach.Id },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = member.Id },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = muted.Id },
            new HubGroupUserMember { HubGroupId = group.Id, UserId = off.Id });
        await db.SaveChangesAsync();

        db.HubGroupSwimmerLevels.Add(new HubGroupSwimmerLevel { HubGroupId = group.Id, SwimmerId = levelled.Id, LevelId = level.Id });
        await db.SaveChangesAsync();
        return new Seed(group.Id, coach.Id, levelled.Id, noLevel.Id, other.Id, level.Id, member.Id, muted.Id, off.Id);
    }

    private static HubGroupTrainingRsvp Going(Seed s, int swimmerId, string answer = TrainingRsvpAnswer.Yes) => new()
    {
        HubGroupId = s.GroupId, SessionDate = Day, SessionStart = "10:00", SwimmerId = swimmerId, Answer = answer,
    };

    private static LanePlanInputDto Plan(params (int SwimmerId, int? LaneNo)[] swimmers) => new()
    {
        LaneCount = 2,
        Swimmers = swimmers.Select(w => new LanePlanSwimmerInputDto { SwimmerId = w.SwimmerId, LaneNo = w.LaneNo }).ToList(),
    };

    [Fact]
    public async Task Publish_GoingWithoutLevelOffLane_IsBlocked_UntilPlacedOrLevelled()
    {
        await using var db = CreateDb(nameof(Publish_GoingWithoutLevelOffLane_IsBlocked_UntilPlacedOrLevelled));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);
        // NoLevel идёт и стоит в Unassigned; Other идёт и снят в «Not today» (в плане его нет).
        db.HubGroupTrainingRsvps.AddRange(Going(s, s.Levelled), Going(s, s.NoLevel), Going(s, s.Other));
        await db.SaveChangesAsync();
        await service.SaveAsync(s.GroupId, Day, Plan((s.Levelled, null), (s.NoLevel, null)), s.Coach);

        var check = (await service.GetPublishCheckAsync(s.GroupId, Day))!;
        // С уровнем без дорожки публикации не мешает; порядок — как в составе.
        Assert.Equal([s.NoLevel, s.Other], check.Blockers.Select(b => b.SwimmerId));
        var blocked = await service.PublishAsync(s.GroupId, Day);
        Assert.False(blocked.Success);
        Assert.Equal("Tom and ניב are going but have no level and no lane. Set a level or put them on a lane to publish.", blocked.Error);
        Assert.Null(await service.GetAsync(s.GroupId, Day, isManager: false));

        // Одного — на дорожку, второму — уровень: можно.
        await service.SaveAsync(s.GroupId, Day, Plan((s.Levelled, null), (s.NoLevel, 2)), s.Coach);
        db.HubGroupSwimmerLevels.Add(new HubGroupSwimmerLevel { HubGroupId = s.GroupId, SwimmerId = s.Other, LevelId = s.LevelId });
        await db.SaveChangesAsync();

        Assert.Empty((await service.GetPublishCheckAsync(s.GroupId, Day))!.Blockers);
        var published = await service.PublishAsync(s.GroupId, Day);
        Assert.True(published.Success);
        Assert.True(published.Changed);
        Assert.NotNull(await service.GetAsync(s.GroupId, Day, isManager: false));
        // Повторный Publish — не ошибка и не «сменился».
        Assert.False((await service.PublishAsync(s.GroupId, Day)).Changed);
    }

    [Fact]
    public async Task Publish_NotGoingOrMaybe_DoesNotBlock_LaneBeyondCountDoes()
    {
        await using var db = CreateDb(nameof(Publish_NotGoingOrMaybe_DoesNotBlock_LaneBeyondCountDoes));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);
        // NoLevel «не уверен», Other молчит — оба без уровня и без дорожки, но не «идущие».
        db.HubGroupTrainingRsvps.Add(Going(s, s.NoLevel, TrainingRsvpAnswer.Maybe));
        await db.SaveChangesAsync();
        await service.SaveAsync(s.GroupId, Day, Plan((s.NoLevel, null), (s.Other, null)), s.Coach);
        Assert.Empty((await service.GetPublishCheckAsync(s.GroupId, Day))!.Blockers);

        // Other идёт и стоит на дорожке 2; дорожек стало одна — строка осталась, но дорожки нет.
        db.HubGroupTrainingRsvps.Add(Going(s, s.Other));
        await db.SaveChangesAsync();
        await service.SaveAsync(s.GroupId, Day, Plan((s.Other, 2)), s.Coach);
        Assert.Empty((await service.GetPublishCheckAsync(s.GroupId, Day))!.Blockers);
        var plan = await db.LanePlans.SingleAsync();
        plan.LaneCount = 1;
        await db.SaveChangesAsync();
        Assert.Equal([s.Other], (await service.GetPublishCheckAsync(s.GroupId, Day))!.Blockers.Select(b => b.SwimmerId));
    }

    [Fact]
    public async Task Publish_NoPlan_NotFound()
    {
        await using var db = CreateDb(nameof(Publish_NoPlan_NotFound));
        var s = await SeedAsync(db);
        var service = new LanePlanService(db);

        Assert.Null(await service.GetPublishCheckAsync(s.GroupId, Day));
        Assert.False((await service.PublishAsync(s.GroupId, Day)).Found);
    }

    [Theory]
    [InlineData(1, "A is going but has no level and no lane. Set a level or put them on a lane to publish.")]
    [InlineData(3, "A, B and C are going but have no level and no lane. Set a level or put them on a lane to publish.")]
    [InlineData(5, "A, B, C and 2 more are going but have no level and no lane. Set a level or put them on a lane to publish.")]
    public void PublishBlockedMessage_NamesUpToThree(int count, string expected)
    {
        var names = new[] { "A", "B", "C", "D", "E" }.Take(count).ToList();

        Assert.Equal(expected, LanePlanRules.PublishBlockedMessage(names));
    }

    // ── Push «дорожки готовы» ────────────────────────────────────────────────

    private static UserPushSubscription Device(int userId, string name) =>
        new() { UserId = userId, Endpoint = $"https://push.example/{name}", P256dh = "p", Auth = "a" };

    [Fact]
    public async Task Notify_SendsToMembersWithDevices_SkipsPublisherMutedAndOff()
    {
        await using var db = CreateDb(nameof(Notify_SendsToMembersWithDevices_SkipsPublisherMutedAndOff));
        var s = await SeedAsync(db);
        db.UserPushSubscriptions.AddRange(
            Device(s.Coach, "coach"), Device(s.Member, "member-phone"), Device(s.Member, "member-gone"),
            Device(s.Muted, "muted"), Device(s.Off, "off"));
        db.HubGroupReminderMutes.Add(new HubGroupReminderMute { UserId = s.Muted, HubGroupId = s.GroupId });
        db.UserNotificationSettings.Add(new UserNotificationSettings { UserId = s.Off, RemindersEnabled = false });
        await db.SaveChangesAsync();
        var sender = new PushSubscriptionServiceTests.FakeSender(e => e.EndsWith("gone") ? PushDeliveryOutcome.Gone : PushDeliveryOutcome.Delivered);

        var reached = await new LanePlanNotifier(db, sender, NullLogger<LanePlanNotifier>.Instance)
            .NotifyPublishedAsync(s.GroupId, Day, s.Coach);

        Assert.Equal(1, reached);
        Assert.Equal(
            ["https://push.example/member-gone", "https://push.example/member-phone"],
            sender.Sent.Select(x => x.Target.Endpoint).Order());
        var payload = sender.Sent[0].Payload;
        Assert.Equal("Dolphin", payload.Title);
        Assert.Equal("Lanes for Sun, Sep 27 are ready", payload.Body);
        Assert.Equal("/groups/dolphin?tab=trainings", payload.Url);
        Assert.Equal($"lanes-{s.GroupId}-2026-09-27", payload.Tag);
        Assert.False(await db.UserPushSubscriptions.AnyAsync(d => d.Endpoint.EndsWith("gone")));
    }
}
