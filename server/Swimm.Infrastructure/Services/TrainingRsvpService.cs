using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Ответы на занятие группы (<see cref="ITrainingRsvpService"/>). Пишет и читает через
/// <see cref="SwimmDbContext"/>: таблица и участники-аккаунты — <c>Sys_</c>, роль swimm_ro их
/// не видит. Без кэша — данные личные и меняются кликом.
/// </summary>
public class TrainingRsvpService : ITrainingRsvpService
{
    private readonly SwimmDbContext _db;

    public TrainingRsvpService(SwimmDbContext db) => _db = db;

    private static DateTime Now(DateTime? nowLocal) => nowLocal ?? IsraelTime.ToLocal(DateTime.UtcNow);

    private async Task<GroupTrainingSchedule?> ScheduleAsync(int hubGroupId)
    {
        var row = await _db.HubGroups.AsNoTracking()
            .Where(g => g.Id == hubGroupId)
            .Select(g => new { g.TrainingSchedule })
            .FirstOrDefaultAsync();
        return row == null ? null : GroupTrainingSchedule.Parse(row.TrainingSchedule);
    }

    public async Task<TrainingRsvpDto?> GetAsync(int hubGroupId, string sessionKey, int viewerUserId, bool isManager,
        DateTime? nowLocal = null)
    {
        if (!TrainingRsvpRules.TryParseSessionKey(sessionKey, out var date, out var start)) return null;
        var schedule = await ScheduleAsync(hubGroupId);
        var now = Now(nowLocal);
        if (schedule == null || !TrainingRsvpRules.IsViewable(schedule, date, start, now)) return null;

        return await BuildAsync(hubGroupId, schedule, date, start, viewerUserId, isManager, now);
    }

    public async Task<TrainingRsvpSaveResult> SetAsync(int hubGroupId, string sessionKey, int actorUserId,
        bool isManager, TrainingRsvpInputDto input, DateTime? nowLocal = null)
    {
        if (!TrainingRsvpRules.TryParseSessionKey(sessionKey, out var date, out var start))
            return TrainingRsvpSaveResult.Fail(404, "Unknown training.");
        var schedule = await ScheduleAsync(hubGroupId);
        if (schedule == null) return TrainingRsvpSaveResult.Fail(404, "Group not found.");

        var now = Now(nowLocal);
        var blocked = TrainingRsvpRules.EditBlockReason(schedule, date, start, now, isManager);
        if (blocked != null) return TrainingRsvpSaveResult.Fail(400, blocked);

        var answer = input.Answer?.Trim().ToLowerInvariant();
        if (answer != null && !TrainingRsvpAnswer.All.Contains(answer))
            return TrainingRsvpSaveResult.Fail(400, "Answer must be yes, maybe, no or empty.");
        var note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim().ToLowerInvariant();
        if (note != null && !TrainingRsvpNote.All.Contains(note))
            return TrainingRsvpSaveResult.Fail(400, "Unknown note.");
        // Заметка («опоздаю», «только первый час») — к ответу «иду» или «не уверен»; к «не
        // приду» она бессмысленна и молча снимается.
        if (answer is null or TrainingRsvpAnswer.No) note = null;

        // За другого отвечает только управляющий (тренер по просьбе в WhatsApp), и только за
        // активного участника. Сам за себя — только участник: управляющий вне состава в
        // полосе не считается, и его ответ повис бы невидимым.
        var targetUserId = input.UserId ?? actorUserId;
        var forOther = targetUserId != actorUserId;
        if (forOther && !isManager)
            return TrainingRsvpSaveResult.Fail(403, "Only the group coach can answer for another member.");

        var targetIsMember = await IsActiveMemberAsync(hubGroupId, targetUserId);
        if (!targetIsMember)
            return TrainingRsvpSaveResult.Fail(400, forOther
                ? "This person is not an active member of the group."
                : "Only group members answer. Join the group first.");

        var row = await _db.HubGroupTrainingRsvps.FirstOrDefaultAsync(r =>
            r.HubGroupId == hubGroupId && r.SessionDate == date && r.SessionStart == start && r.UserId == targetUserId);

        if (answer == null)
        {
            if (row != null) _db.HubGroupTrainingRsvps.Remove(row);
        }
        else
        {
            if (row == null)
            {
                row = new HubGroupTrainingRsvp
                {
                    HubGroupId = hubGroupId, SessionDate = date, SessionStart = start, UserId = targetUserId,
                };
                _db.HubGroupTrainingRsvps.Add(row);
            }
            row.Answer = answer;
            row.Note = note;
            row.SetByUserId = forOther ? actorUserId : null;
            row.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();

        return TrainingRsvpSaveResult.Ok(await BuildAsync(hubGroupId, schedule, date, start, actorUserId, isManager, now));
    }

    private Task<bool> IsActiveMemberAsync(int hubGroupId, int userId) =>
        _db.HubGroupUserMembers.AnyAsync(m =>
            m.HubGroupId == hubGroupId && m.UserId == userId && m.Status == HubGroupUserMemberStatus.Active);

    /// <summary>Порядок групп списка у управляющего: иду → не уверен → не приду → без ответа.</summary>
    private static int AnswerOrder(string? answer) => answer switch
    {
        TrainingRsvpAnswer.Yes => 0,
        TrainingRsvpAnswer.Maybe => 1,
        TrainingRsvpAnswer.No => 2,
        _ => 3,
    };

    private async Task<TrainingRsvpDto> BuildAsync(int hubGroupId, GroupTrainingSchedule schedule,
        DateOnly date, string start, int viewerUserId, bool isManager, DateTime now)
    {
        // Знаменатель полосы — активные участники-аккаунты. Ответы ушедших (и pending) не
        // считаются: полоса «13 из 18» обязана складываться из людей состава.
        var members = await _db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && m.Status == HubGroupUserMemberStatus.Active)
            .Select(m => new
            {
                m.UserId,
                Name = m.User!.DisplayName,
                // Пол — для цвета аватара: пловец, за которого аккаунт в группе, иначе свой.
                Gender = m.Swimmer != null ? m.Swimmer.Gender : (m.User.Swimmer != null ? m.User.Swimmer.Gender : null),
            })
            .ToListAsync();
        var memberIds = members.Select(m => m.UserId).ToHashSet();

        var answers = (await _db.HubGroupTrainingRsvps.AsNoTracking()
                .Where(r => r.HubGroupId == hubGroupId && r.SessionDate == date && r.SessionStart == start)
                .ToListAsync())
            .Where(r => memberIds.Contains(r.UserId))
            .ToDictionary(r => r.UserId);

        var slot = TrainingRsvpRules.FindSlot(schedule, date, start)!;
        var isMember = memberIds.Contains(viewerUserId);
        answers.TryGetValue(viewerUserId, out var mine);

        return new TrainingRsvpDto
        {
            SessionId = TrainingRsvpRules.SessionKey(date, start),
            Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Start = start,
            End = slot.End,
            Yes = answers.Values.Count(r => r.Answer == TrainingRsvpAnswer.Yes),
            Maybe = answers.Values.Count(r => r.Answer == TrainingRsvpAnswer.Maybe),
            No = answers.Values.Count(r => r.Answer == TrainingRsvpAnswer.No),
            Total = memberIds.Count,
            Mine = mine == null ? null : new TrainingRsvpMineDto
            {
                Answer = mine.Answer, Note = mine.Note, SetByCoach = mine.SetByUserId != null,
            },
            IsMember = isMember,
            CanManage = isManager,
            CanAnswer = (isMember || isManager)
                        && TrainingRsvpRules.EditBlockReason(schedule, date, start, now, isManager) == null,
            People = !isManager ? null : members
                .Select(m =>
                {
                    answers.TryGetValue(m.UserId, out var a);
                    return new TrainingRsvpPersonDto
                    {
                        UserId = m.UserId,
                        Name = m.Name,
                        Gender = m.Gender is "male" or "female" ? m.Gender : null,
                        Answer = a?.Answer,
                        Note = a?.Note,
                        SetByCoach = a?.SetByUserId != null,
                    };
                })
                .OrderBy(p => AnswerOrder(p.Answer))
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
        };
    }
}
