using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Подписки и настройки напоминаний (<see cref="PushSubscriptionService"/>,
/// docs/plans/web-push-reminders-plan.md §6): upsert по endpoint, переезд браузера к другому
/// аккаунту, потолок устройств, «Send test» удаляет умершие подписки, умолчания настроек.
/// UNIQUE и CHECK InMemory не видит — они в <see cref="WebPushPgTests"/>.
/// </summary>
public class PushSubscriptionServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>Отправитель-заглушка: исход по адресу подписки, всё отправленное — в список.</summary>
    internal sealed class FakeSender(Func<string, PushDeliveryOutcome>? outcome = null) : IPushSender
    {
        public List<(PushTarget Target, PushPayload Payload)> Sent { get; } = [];
        public bool IsConfigured => true;
        public string? PublicKey => "BPublicKey";

        public Task<PushDeliveryOutcome> SendAsync(PushTarget target, PushPayload payload, CancellationToken ct = default)
        {
            Sent.Add((target, payload));
            return Task.FromResult(outcome?.Invoke(target.Endpoint) ?? PushDeliveryOutcome.Delivered);
        }
    }

    private const string P256 = "BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM";
    private const string Auth = "tBHItJI5svbpez7KI4CCXg";
    private const string Iphone = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 Version/17.4 Mobile/15E148 Safari/604.1";

    private static PushSubscriptionInputDto Sub(string endpoint) => new()
    {
        Endpoint = endpoint,
        Keys = new PushSubscriptionKeysDto { P256dh = P256, Auth = Auth },
    };

    [Fact]
    public async Task Subscribe_Upserts_ByEndpoint_AndMovesBrowserToNewUser()
    {
        await using var db = CreateDb(nameof(Subscribe_Upserts_ByEndpoint_AndMovesBrowserToNewUser));
        var svc = new PushSubscriptionService(db, new FakeSender());
        const string ep = "https://fcm.googleapis.com/fcm/send/one";

        Assert.Null(await svc.SubscribeAsync(1, Sub(ep), Iphone));
        Assert.Null(await svc.SubscribeAsync(1, Sub(ep), Iphone));   // повтор — та же строка
        var row = Assert.Single(db.UserPushSubscriptions);
        Assert.Equal((1, "iPhone · Safari"), (row.UserId, row.DeviceLabel));

        Assert.Null(await svc.SubscribeAsync(2, Sub(ep), Iphone));   // другой человек в том же браузере
        Assert.Equal(2, Assert.Single(db.UserPushSubscriptions).UserId);
    }

    [Fact]
    public async Task Subscribe_RejectsForeignHost_AndBadKeys()
    {
        await using var db = CreateDb(nameof(Subscribe_RejectsForeignHost_AndBadKeys));
        var svc = new PushSubscriptionService(db, new FakeSender());

        Assert.NotNull(await svc.SubscribeAsync(1, Sub("https://example.com/push"), null));
        Assert.NotNull(await svc.SubscribeAsync(1, new PushSubscriptionInputDto
        {
            Endpoint = "https://fcm.googleapis.com/fcm/send/x",
            Keys = new PushSubscriptionKeysDto { P256dh = "not base64!", Auth = Auth },
        }, null));
        Assert.Empty(db.UserPushSubscriptions);
    }

    [Fact]
    public async Task Subscribe_OverLimit_DropsOldest()
    {
        await using var db = CreateDb(nameof(Subscribe_OverLimit_DropsOldest));
        var svc = new PushSubscriptionService(db, new FakeSender());
        for (var i = 0; i < PushRules.MaxDevicesPerUser + 2; i++)
        {
            Assert.Null(await svc.SubscribeAsync(1, Sub($"https://fcm.googleapis.com/fcm/send/{i}"), null));
            await Task.Delay(2);   // CreatedAt — порядок вытеснения
        }

        var left = await db.UserPushSubscriptions.Select(s => s.Endpoint).ToListAsync();
        Assert.Equal(PushRules.MaxDevicesPerUser, left.Count);
        Assert.DoesNotContain("https://fcm.googleapis.com/fcm/send/0", left);
        Assert.DoesNotContain("https://fcm.googleapis.com/fcm/send/1", left);
    }

    [Fact]
    public async Task SendTest_DeliversToAllOwnDevices_RemovesGone()
    {
        await using var db = CreateDb(nameof(SendTest_DeliversToAllOwnDevices_RemovesGone));
        const string alive = "https://fcm.googleapis.com/fcm/send/alive";
        const string dead = "https://web.push.apple.com/dead";
        var sender = new FakeSender(ep => ep == dead ? PushDeliveryOutcome.Gone : PushDeliveryOutcome.Delivered);
        var svc = new PushSubscriptionService(db, sender);
        await svc.SubscribeAsync(1, Sub(alive), null);
        await svc.SubscribeAsync(1, Sub(dead), null);
        await svc.SubscribeAsync(2, Sub("https://fcm.googleapis.com/fcm/send/other"), null);

        var result = await svc.SendTestAsync(1);

        Assert.Equal((2, 1, 1, 0), (result.Devices, result.Delivered, result.Removed, result.Failed));
        Assert.Equal(2, sender.Sent.Count);   // чужому устройству — ничего
        var mine = Assert.Single(db.UserPushSubscriptions, s => s.UserId == 1);
        Assert.Equal(alive, mine.Endpoint);
        Assert.NotNull(mine.LastSuccessAt);
    }

    [Fact]
    public async Task Settings_DefaultsWithoutRow_SaveValidates_MarksCurrentDevice()
    {
        await using var db = CreateDb(nameof(Settings_DefaultsWithoutRow_SaveValidates_MarksCurrentDevice));
        var svc = new PushSubscriptionService(db, new FakeSender());
        const string ep = "https://fcm.googleapis.com/fcm/send/me";
        await svc.SubscribeAsync(1, Sub(ep), Iphone);

        var initial = await svc.GetSettingsAsync(1, ep);
        Assert.Equal((true, "2h", false, true), (initial.RemindersEnabled, initial.Lead, initial.EmailFallback, initial.PushConfigured));
        Assert.True(Assert.Single(initial.Devices).IsCurrent);

        Assert.NotNull(await svc.SaveSettingsAsync(1, new NotificationSettingsInputDto { Lead = "3h" }));
        Assert.Null(await svc.SaveSettingsAsync(1, new NotificationSettingsInputDto { Lead = "evening" }));
        Assert.Null(await svc.SaveSettingsAsync(1, new NotificationSettingsInputDto { RemindersEnabled = false }));
        await svc.SetGroupMutedAsync(1, 7, remind: false);
        await svc.SetGroupMutedAsync(1, 7, remind: false);   // повтор — без ошибки

        var saved = await svc.GetSettingsAsync(1, null);
        Assert.Equal((false, "evening"), (saved.RemindersEnabled, saved.Lead));
        Assert.Equal([7], saved.MutedGroupIds);
        Assert.False(Assert.Single(saved.Devices).IsCurrent);

        await svc.SetGroupMutedAsync(1, 7, remind: true);
        Assert.Empty((await svc.GetSettingsAsync(1, null)).MutedGroupIds);
    }

    [Fact]
    public async Task Unsubscribe_And_RemoveDevice_OnlyOwn()
    {
        await using var db = CreateDb(nameof(Unsubscribe_And_RemoveDevice_OnlyOwn));
        var svc = new PushSubscriptionService(db, new FakeSender());
        const string ep = "https://fcm.googleapis.com/fcm/send/mine";
        await svc.SubscribeAsync(1, Sub(ep), null);
        var id = (await db.UserPushSubscriptions.SingleAsync()).Id;

        await svc.UnsubscribeAsync(2, ep);
        Assert.False(await svc.RemoveDeviceAsync(2, id));
        Assert.Single(db.UserPushSubscriptions);

        Assert.True(await svc.RemoveDeviceAsync(1, id));
        Assert.Empty(db.UserPushSubscriptions);
    }
}
