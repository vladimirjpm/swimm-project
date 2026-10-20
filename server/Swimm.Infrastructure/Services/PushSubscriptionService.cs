using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Push-подписки и настройки напоминаний (<see cref="IPushSubscriptionService"/>,
/// docs/plans/web-push-reminders-plan.md §6). Таблицы Sys_ — через <see cref="SwimmDbContext"/>,
/// без кэша: данные личные и меняются кликом.
/// </summary>
public class PushSubscriptionService(SwimmDbContext db, IPushSender sender) : IPushSubscriptionService
{
    public async Task<string?> SubscribeAsync(int userId, PushSubscriptionInputDto input, string? userAgent,
        CancellationToken ct = default)
    {
        var endpoint = input.Endpoint?.Trim();
        var p256dh = input.Keys?.P256dh?.Trim();
        var auth = input.Keys?.Auth?.Trim();
        if (!PushRules.IsAllowedEndpoint(endpoint)) return "This browser's push service is not supported.";
        if (!PushRules.IsBase64Url(p256dh, PushRules.KeyMax) || !PushRules.IsBase64Url(auth, PushRules.AuthMax))
            return "Invalid subscription keys.";

        var label = PushRules.DeviceLabel(userAgent);
        var row = await db.UserPushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        if (row == null)
        {
            row = new UserPushSubscription { Endpoint = endpoint!, CreatedAt = DateTime.UtcNow };
            db.UserPushSubscriptions.Add(row);
        }
        else if (row.UserId != userId)
        {
            // Тот же браузер, другой человек вошёл и включил: устройство теперь его, история доставки — с нуля.
            row.CreatedAt = DateTime.UtcNow;
            row.LastSuccessAt = null;
        }
        row.UserId = userId;
        row.P256dh = p256dh!;
        row.Auth = auth!;
        row.DeviceLabel = label;
        row.FailureCount = 0;
        row.LastFailureAt = null;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (row.Id == 0)
        {
            // Две вкладки одного браузера подписались разом: вторая упёрлась в UNIQUE (Endpoint) —
            // строка уже есть, и она та же самая.
            db.ChangeTracker.Clear();
        }

        // Лишние устройства — самые старые: телефон, который человек давно сменил.
        var extra = await db.UserPushSubscriptions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Skip(PushRules.MaxDevicesPerUser)
            .ToListAsync(ct);
        if (extra.Count > 0)
        {
            db.UserPushSubscriptions.RemoveRange(extra);
            await db.SaveChangesAsync(ct);
        }
        return null;
    }

    public async Task UnsubscribeAsync(int userId, string endpoint, CancellationToken ct = default)
    {
        var row = await db.UserPushSubscriptions.FirstOrDefaultAsync(
            s => s.UserId == userId && s.Endpoint == endpoint, ct);
        if (row == null) return;
        db.UserPushSubscriptions.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemoveDeviceAsync(int userId, int deviceId, CancellationToken ct = default)
    {
        var row = await db.UserPushSubscriptions.FirstOrDefaultAsync(s => s.UserId == userId && s.Id == deviceId, ct);
        if (row == null) return false;
        db.UserPushSubscriptions.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<NotificationSettingsDto> GetSettingsAsync(int userId, string? currentEndpoint,
        CancellationToken ct = default)
    {
        var settings = await db.UserNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
        var devices = await db.UserPushSubscriptions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Select(s => new { s.Id, s.Endpoint, s.DeviceLabel, s.CreatedAt, s.LastSuccessAt, s.LastFailureAt })
            .ToListAsync(ct);
        var muted = await db.HubGroupReminderMutes.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.HubGroupId)
            .Select(m => m.HubGroupId)
            .ToListAsync(ct);

        return new NotificationSettingsDto
        {
            RemindersEnabled = settings?.RemindersEnabled ?? TrainingReminderRules.Settings.Default.Enabled,
            Lead = settings?.LeadKind ?? ReminderLead.Default,
            EmailFallback = settings?.EmailFallback ?? false,
            MutedGroupIds = muted,
            PushConfigured = sender.IsConfigured,
            Devices = devices.Select(d => new PushDeviceDto
            {
                Id = d.Id,
                Label = d.DeviceLabel ?? "Device",
                CreatedAt = DateTime.SpecifyKind(d.CreatedAt, DateTimeKind.Utc),
                LastSuccessAt = d.LastSuccessAt is { } ok ? DateTime.SpecifyKind(ok, DateTimeKind.Utc) : null,
                LastFailureAt = d.LastFailureAt is { } bad ? DateTime.SpecifyKind(bad, DateTimeKind.Utc) : null,
                IsCurrent = currentEndpoint != null && d.Endpoint == currentEndpoint,
            }).ToList(),
        };
    }

    public async Task<string?> SaveSettingsAsync(int userId, NotificationSettingsInputDto input, CancellationToken ct = default)
    {
        var lead = input.Lead?.Trim().ToLowerInvariant();
        if (lead != null && !ReminderLead.All.Contains(lead)) return "Lead must be 1h, 2h or evening.";

        var row = await db.UserNotificationSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (row == null)
        {
            row = new UserNotificationSettings { UserId = userId };
            db.UserNotificationSettings.Add(row);
        }
        if (input.RemindersEnabled is bool enabled) row.RemindersEnabled = enabled;
        if (lead != null) row.LeadKind = lead;
        if (input.EmailFallback is bool email) row.EmailFallback = email;
        row.UpdatedAt = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (db.Entry(row).State == EntityState.Added)
        {
            // Две вкладки сохранили первую строку разом — вторая правит победившую.
            db.ChangeTracker.Clear();
            return await SaveSettingsAsync(userId, input, ct);
        }
        return null;
    }

    public async Task SetGroupMutedAsync(int userId, int hubGroupId, bool remind, CancellationToken ct = default)
    {
        var row = await db.HubGroupReminderMutes.FirstOrDefaultAsync(m => m.UserId == userId && m.HubGroupId == hubGroupId, ct);
        if (remind)
        {
            if (row == null) return;
            db.HubGroupReminderMutes.Remove(row);
        }
        else
        {
            if (row != null) return;
            db.HubGroupReminderMutes.Add(new HubGroupReminderMute { UserId = userId, HubGroupId = hubGroupId });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (!remind)
        {
            db.ChangeTracker.Clear();   // уже отключена соседней вкладкой
        }
    }

    public async Task<PushTestResultDto> SendTestAsync(int userId, CancellationToken ct = default)
    {
        var rows = await db.UserPushSubscriptions.Where(s => s.UserId == userId).ToListAsync(ct);
        var result = new PushTestResultDto { Devices = rows.Count };
        var payload = new PushPayload("SwimHub", "Test reminder: notifications work on this device.",
            "/reminders", "swimhub-test");

        foreach (var row in rows)
        {
            var outcome = await sender.SendAsync(new PushTarget(row.Endpoint, row.P256dh, row.Auth), payload, ct);
            PushDeliveryBookkeeping.Apply(db, row, outcome, DateTime.UtcNow);
            switch (outcome)
            {
                case PushDeliveryOutcome.Delivered: result.Delivered++; break;
                case PushDeliveryOutcome.Gone: result.Removed++; break;
                default: result.Failed++; break;
            }
        }
        await db.SaveChangesAsync(ct);
        return result;
    }
}

/// <summary>
/// Что делать с подпиской после отправки — одно место для «Send test» и джоба напоминаний:
/// принято — отметка успеха; 404/410 — строку удалить (§2 плана); сбой — счётчик неудач.
/// Сохраняет вызывающий.
/// </summary>
internal static class PushDeliveryBookkeeping
{
    public static void Apply(SwimmDbContext db, UserPushSubscription row, PushDeliveryOutcome outcome, DateTime nowUtc)
    {
        switch (outcome)
        {
            case PushDeliveryOutcome.Delivered:
                row.LastSuccessAt = nowUtc;
                row.FailureCount = 0;
                break;
            case PushDeliveryOutcome.Gone:
                db.UserPushSubscriptions.Remove(row);
                break;
            default:
                row.LastFailureAt = nowUtc;
                row.FailureCount++;
                break;
        }
    }
}
