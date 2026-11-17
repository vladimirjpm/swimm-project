using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="ILanePlanNotifier"/>
public class LanePlanNotifier(SwimmDbContext db, IPushSender sender, ILogger<LanePlanNotifier> logger)
    : ILanePlanNotifier
{
    public async Task<int> NotifyPublishedAsync(
        int hubGroupId, LanePlanKey key, int publisherUserId, CancellationToken ct = default)
    {
        if (!sender.IsConfigured) return 0;

        var group = await db.HubGroups.AsNoTracking()
            .Where(g => g.Id == hubGroupId)
            .Select(g => new { g.Name, g.Slug })
            .FirstOrDefaultAsync(ct);
        if (group == null) return 0;

        var memberIds = await db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && m.Status == HubGroupUserMemberStatus.Active
                        && m.UserId != publisherUserId)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct);
        if (memberIds.Count == 0) return 0;

        var muted = await db.HubGroupReminderMutes.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && memberIds.Contains(m.UserId))
            .Select(m => m.UserId)
            .ToListAsync(ct);
        var off = await db.UserNotificationSettings.AsNoTracking()
            .Where(s => memberIds.Contains(s.UserId) && !s.RemindersEnabled)
            .Select(s => s.UserId)
            .ToListAsync(ct);
        var recipients = memberIds.Except(muted).Except(off).ToList();
        if (recipients.Count == 0) return 0;

        var devices = await db.UserPushSubscriptions.Where(s => recipients.Contains(s.UserId)).ToListAsync(ct);
        if (devices.Count == 0) return 0;

        var m = LanePlanRules.PublishedPush(hubGroupId, group.Name, group.Slug, key);
        var payload = new PushPayload(m.Title, m.Body, m.Url, m.Tag);

        var reached = new HashSet<int>();
        foreach (var device in devices)
        {
            try
            {
                var outcome = await sender.SendAsync(new PushTarget(device.Endpoint, device.P256dh, device.Auth), payload, ct);
                PushDeliveryBookkeeping.Apply(db, device, outcome, DateTime.UtcNow);
                if (outcome == PushDeliveryOutcome.Delivered) reached.Add(device.UserId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // План уже опубликован — сбой пуша его не отменяет.
                logger.LogWarning(ex, "Lane plan push to user {UserId} failed", device.UserId);
            }
        }
        await db.SaveChangesAsync(ct);
        return reached.Count;
    }
}
