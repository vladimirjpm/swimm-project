using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="ICoachRequestNotifier"/>
public class CoachRequestNotifier(SwimmDbContext db, IPushSender sender, ILogger<CoachRequestNotifier> logger)
    : ICoachRequestNotifier
{
    public async Task<int> NotifyOwnerAsync(int hubGroupId, int requesterUserId, CancellationToken ct = default)
    {
        if (!sender.IsConfigured) return 0;

        var group = await db.HubGroups.AsNoTracking()
            .Where(g => g.Id == hubGroupId)
            .Select(g => new { g.OwnerUserId, g.Name, g.Slug })
            .FirstOrDefaultAsync(ct);
        // Сам себе заявку не шлёт (владелец получит отказ раньше), но страховка дешёвая.
        if (group?.OwnerUserId is not int ownerId || ownerId == requesterUserId) return 0;

        var rows = await db.UserPushSubscriptions.Where(s => s.UserId == ownerId).ToListAsync(ct);
        if (rows.Count == 0) return 0;

        var requester = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == requesterUserId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct);
        var payload = new PushPayload(
            "Coach request",
            $"{(string.IsNullOrWhiteSpace(requester) ? "Someone" : requester)} asks to coach {group.Name}",
            $"/groups/{Uri.EscapeDataString(group.Slug)}?tab=admin",
            $"coach-req:{hubGroupId}");

        var delivered = 0;
        foreach (var row in rows)
        {
            try
            {
                var outcome = await sender.SendAsync(new PushTarget(row.Endpoint, row.P256dh, row.Auth), payload, ct);
                PushDeliveryBookkeeping.Apply(db, row, outcome, DateTime.UtcNow);
                if (outcome == PushDeliveryOutcome.Delivered) delivered++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Заявка уже записана — сбой пуша её не отменяет.
                logger.LogWarning(ex, "Coach request push to user {UserId} failed", ownerId);
            }
        }
        await db.SaveChangesAsync(ct);
        return delivered;
    }
}
