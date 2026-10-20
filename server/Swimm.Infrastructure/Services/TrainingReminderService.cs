using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Прогон напоминаний о тренировках (<see cref="ITrainingReminderService"/>,
/// docs/plans/web-push-reminders-plan.md §7). Собирает данные пачкой, решает чистое правило
/// <see cref="TrainingReminderRules.Plan"/>, затем на каждое напоминание: СНАЧАЛА строка в
/// <c>Sys_ReminderDeliveries</c> (UNIQUE по занятию), ПОТОМ отправка. Не вставилось — уже
/// отправил другой процесс (ProdAdmin пишет в ту же базу) или прогон до рестарта (§4 п.2).
///
/// Кого считаем: только аккаунты, у которых есть хоть одна push-подписка — остальным слать
/// нечем (письмо — П4). Отсюда и порядок запросов: от подписок к группам, а не наоборот.
/// </summary>
public class TrainingReminderService(SwimmDbContext db, IPushSender sender, ILogger<TrainingReminderService> logger)
    : ITrainingReminderService
{
    public async Task<TrainingReminderReport> RunAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        await PruneJournalAsync(nowUtc, ct);
        if (!sender.IsConfigured) return new TrainingReminderReport(0, 0, 0, 0, 0, 0);

        var nowLocal = IsraelTime.ToLocal(nowUtc);
        var today = DateOnly.FromDateTime(nowLocal);
        var last = today.AddDays(TrainingReminderRules.DaysAhead);

        var usersWithDevices = (await db.UserPushSubscriptions.AsNoTracking()
            .Select(s => s.UserId).Distinct().ToListAsync(ct)).ToHashSet();
        if (usersWithDevices.Count == 0) return new TrainingReminderReport(0, 0, 0, 0, 0, 0);

        var memberships = await db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.Status == HubGroupUserMemberStatus.Active && usersWithDevices.Contains(m.UserId))
            .Select(m => new { m.HubGroupId, m.UserId })
            .ToListAsync(ct);
        var members = memberships.GroupBy(m => m.HubGroupId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<int>)g.Select(m => m.UserId).Distinct().ToList());
        var groupIds = members.Keys.ToList();
        if (groupIds.Count == 0) return new TrainingReminderReport(0, 0, 0, 0, 0, 0);

        var groups = await LoadGroupsAsync(groupIds, today, last, ct);

        // Перерывы и ответы нужны только там, где напоминание уже может быть пора хоть кому-то
        // (самый ранний вид — вечер накануне / 21:00 перед тихими часами), — не на все дни окна.
        var candidates = groups
            .SelectMany(g => TrainingReminderRules.UpcomingStarts(g, nowLocal)
                .Where(o => ReminderLead.All.Min(l => TrainingReminderRules.DueAt(o.StartLocal, l)) <= nowLocal)
                .Select(o => (g.HubGroupId, o.Occurrence.Date)))
            .Distinct()
            .ToList();
        if (candidates.Count == 0) return new TrainingReminderReport(groups.Count, 0, 0, 0, 0, 0);

        var onBreak = new Dictionary<(int, DateOnly), IReadOnlySet<int>>();
        foreach (var (groupId, date) in candidates)
            onBreak[(groupId, date)] = (await HubGroupBreakQuery.LoadAsync(db, groupId, date)).UserIds;

        var candidateGroups = candidates.Select(c => c.HubGroupId).Distinct().ToList();
        var answers = (await db.HubGroupTrainingRsvps.AsNoTracking()
                .Where(r => candidateGroups.Contains(r.HubGroupId) && r.UserId != null
                            && r.SessionDate >= today && r.SessionDate <= last)
                .Select(r => new { r.HubGroupId, UserId = r.UserId!.Value, r.SessionDate, r.SessionStart, r.Answer })
                .ToListAsync(ct))
            .ToDictionary(r => (r.HubGroupId, r.UserId, r.SessionDate, r.SessionStart), r => r.Answer);

        var settings = (await db.UserNotificationSettings.AsNoTracking()
                .Where(s => usersWithDevices.Contains(s.UserId))
                .Select(s => new { s.UserId, s.RemindersEnabled, s.LeadKind })
                .ToListAsync(ct))
            .ToDictionary(s => s.UserId, s => new TrainingReminderRules.Settings(s.RemindersEnabled, s.LeadKind));
        var muted = (await db.HubGroupReminderMutes.AsNoTracking()
                .Where(m => usersWithDevices.Contains(m.UserId))
                .Select(m => new { m.UserId, m.HubGroupId })
                .ToListAsync(ct))
            .Select(m => (m.UserId, m.HubGroupId)).ToHashSet();
        var delivered = (await db.ReminderDeliveries.AsNoTracking()
                .Where(d => d.Kind == ReminderKind.Reminder && d.SessionDate >= today && candidateGroups.Contains(d.HubGroupId))
                .Select(d => new { d.UserId, d.HubGroupId, d.SessionDate, d.SessionStart })
                .ToListAsync(ct))
            .Select(d => (d.UserId, d.HubGroupId, d.SessionDate, d.SessionStart)).ToHashSet();

        var people = new TrainingReminderRules.People(members, usersWithDevices, settings, muted, onBreak, answers, delivered);
        var planned = TrainingReminderRules.Plan(groups, people, nowLocal);

        int sent = 0, skipped = 0, failed = 0, removed = 0;
        foreach (var p in planned)
        {
            ct.ThrowIfCancellationRequested();
            var outcome = await DeliverAsync(p, nowLocal, ct);
            switch (outcome.Result)
            {
                case null: skipped++; break;
                case ReminderResult.Sent: sent++; break;
                default: failed++; break;
            }
            removed += outcome.Removed;
        }

        if (planned.Count > 0)
            logger.LogInformation("Напоминания: запланировано {Planned}, отправлено {Sent}, уже было {Skipped}, не доставлено {Failed}",
                planned.Count, sent, skipped, failed);
        return new TrainingReminderReport(groups.Count, planned.Count, sent, skipped, failed, removed);
    }

    /// <summary>
    /// Застолбить строку журнала и разослать на все устройства человека. Result null — строку
    /// вставить не удалось (UNIQUE): напоминание уже отправлено, ничего не шлём.
    /// </summary>
    private async Task<(string? Result, int Removed)> DeliverAsync(
        TrainingReminderRules.Planned p, DateTime nowLocal, CancellationToken ct)
    {
        var row = new ReminderDelivery
        {
            UserId = p.UserId,
            HubGroupId = p.Group.HubGroupId,
            SessionDate = p.Occurrence.Date,
            SessionStart = p.Occurrence.Start,
            Kind = ReminderKind.Reminder,
            Channel = "push",
            CreatedAt = DateTime.UtcNow,
            Result = ReminderResult.Pending,
        };
        db.ReminderDeliveries.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return (null, 0);
        }

        var message = TrainingReminderRules.Compose(p, nowLocal);
        var payload = new PushPayload(message.Title, message.Body, message.Url, message.Tag);
        var devices = await db.UserPushSubscriptions.Where(s => s.UserId == p.UserId).ToListAsync(ct);
        int delivered = 0, gone = 0;
        foreach (var device in devices)
        {
            var outcome = await sender.SendAsync(new PushTarget(device.Endpoint, device.P256dh, device.Auth), payload, ct);
            PushDeliveryBookkeeping.Apply(db, device, outcome, DateTime.UtcNow);
            if (outcome == PushDeliveryOutcome.Delivered) delivered++;
            else if (outcome == PushDeliveryOutcome.Gone) gone++;
        }

        row.Devices = devices.Count;
        row.Delivered = delivered;
        row.SentAt = DateTime.UtcNow;
        row.Result = delivered > 0 ? ReminderResult.Sent
            : devices.Count > 0 && gone == devices.Count ? ReminderResult.Gone
            : ReminderResult.Failed;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return (row.Result, gone);
    }

    /// <summary>Группы с расписанием, разовыми занятиями и отменами на [<paramref name="from"/>; <paramref name="to"/>].</summary>
    private async Task<List<TrainingReminderRules.Group>> LoadGroupsAsync(
        List<int> groupIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await db.HubGroups.AsNoTracking()
            .Where(g => groupIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Name, g.Slug, g.TrainingSchedule })
            .ToListAsync(ct);

        // Дата тренировки в базе — полночь UTC (как в TrainingRsvpService.OneOffsAsync).
        var fromDb = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toDb = DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var oneOffs = (await db.TrainingSessions.AsNoTracking()
                .Where(s => groupIds.Contains(s.HubGroupId) && s.AsksRsvp && s.StartTime != null
                            && s.Date >= fromDb && s.Date < toDb)
                .OrderBy(s => s.Id)
                .Select(s => new { s.HubGroupId, s.Id, s.Date, s.StartTime, s.Title, s.Type, s.PoolType })
                .ToListAsync(ct))
            .ToLookup(s => s.HubGroupId, s => new TrainingOccurrences.OneOff(
                s.Id, DateOnly.FromDateTime(s.Date), s.StartTime!, s.Title, s.Type,
                string.IsNullOrWhiteSpace(s.PoolType) ? null : s.PoolType));
        var cancellations = (await db.HubGroupTrainingCancellations.AsNoTracking()
                .Where(c => groupIds.Contains(c.HubGroupId) && c.Date >= from && c.Date <= to)
                .Select(c => new { c.HubGroupId, c.Date, c.Start, c.Reason })
                .ToListAsync(ct))
            .ToLookup(c => c.HubGroupId, c => new TrainingOccurrences.Cancellation(c.Date, c.Start, c.Reason));

        return rows.Select(g => new TrainingReminderRules.Group(
                g.Id, g.Name, g.Slug, GroupTrainingSchedule.Parse(g.TrainingSchedule),
                oneOffs[g.Id].ToList(), cancellations[g.Id].ToList()))
            .ToList();
    }

    /// <summary>
    /// Журнал нужен защите от дубля только на будущие занятия — старше
    /// <see cref="TrainingReminderRules.DeliveryKeepDays"/> дней удаляем порцией (обычным путём:
    /// так же работает и в тестах на InMemory).
    /// </summary>
    private async Task PruneJournalAsync(DateTime nowUtc, CancellationToken ct)
    {
        var before = nowUtc.AddDays(-TrainingReminderRules.DeliveryKeepDays);
        var stale = await db.ReminderDeliveries.Where(d => d.CreatedAt < before).Take(500).ToListAsync(ct);
        if (stale.Count == 0) return;
        db.ReminderDeliveries.RemoveRange(stale);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
}
