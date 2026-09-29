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

        // «Going» на занятие внутри перерыва — человек вернулся (Ш3.1): перерыв закрывается,
        // тренеру — пометка «back after …». Перерыв, который к этой дате и так кончится, не трогаем.
        if (answer == TrainingRsvpAnswer.Yes) await EndBreaksByRsvpAsync(hubGroupId, targetUserId, date);

        await _db.SaveChangesAsync();

        return TrainingRsvpSaveResult.Ok(await BuildAsync(hubGroupId, schedule, date, start, actorUserId, isManager, now));
    }

    /// <summary>
    /// Закрыть открытые перерывы человека на дату занятия: его аккаунта и пловца, которым он стоит
    /// в группе (флаг один на человека — <see cref="HubGroupBreakQuery"/>, <see cref="HubGroupPersonResolver"/>).
    /// </summary>
    private async Task EndBreaksByRsvpAsync(int hubGroupId, int userId, DateOnly sessionDate)
    {
        var labelSwimmer = (await HubGroupPersonResolver.ResolveAsync(_db, hubGroupId, [userId]))[userId];

        var open = await _db.HubGroupBreaks
            .Where(b => b.HubGroupId == hubGroupId && b.EndedAt == null
                        && (b.Until == null || b.Until >= sessionDate)
                        && (b.UserId == userId || (labelSwimmer != null && b.SwimmerId == labelSwimmer)))
            .ToListAsync();
        foreach (var b in open)
        {
            b.EndedAt = DateTime.UtcNow;
            b.EndedByRsvp = true;
        }
    }

    /// <summary>
    /// Вид по дорожкам (Ш3.2): кем каждый пришедший стоит (<see cref="HubGroupPersonResolver"/>),
    /// его уровень (пловца, иначе аккаунта), порядок состава — и чистая раскладка
    /// <see cref="TrainingLaneView"/>. Только в этом личном ответе: имена и ответы — приватные.
    /// </summary>
    private async Task<TrainingLaneViewDto?> BuildLaneViewAsync(int hubGroupId, GroupTrainingSchedule schedule,
        DateOnly date, IReadOnlyDictionary<int, (string Name, string? Gender)> members,
        IReadOnlyDictionary<int, HubGroupTrainingRsvp> answers, int viewerUserId, bool isManager)
    {
        var mode = schedule.EffectiveLaneView;
        if (mode == GroupLaneView.Off) return null;

        var plan = await _db.LanePlans.AsNoTracking()
            .Include(p => p.Lanes)
            .Include(p => p.Swimmers)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.HubGroupId == hubGroupId && p.Date == date && p.Status == LanePlanStatus.Published);
        if (plan == null && mode == GroupLaneView.Plan) return null;
        var lastPlanLanes = plan != null || schedule.UsualLanes != null ? null : await _db.LanePlans.AsNoTracking()
            .Where(p => p.HubGroupId == hubGroupId)
            .OrderByDescending(p => p.Date)
            .Select(p => (int?)p.LaneCount)
            .FirstOrDefaultAsync();

        // Кем стоит каждый участник (не только пришедшие): «2 claim» считается по всей группе.
        var resolved = await HubGroupPersonResolver.ResolveAsync(_db, hubGroupId, members.Keys.ToList());
        var claims = resolved.Values.Where(v => v != null)
            .GroupBy(v => v!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var roster = await _db.HubGroupMembers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && !m.IsExcluded)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Swimmer!.LastName).ThenBy(m => m.Swimmer!.FirstName)
            .ThenBy(m => m.SwimmerId)
            .Select(m => new
            {
                m.SwimmerId,
                Name = (m.Swimmer!.LastName + " " + m.Swimmer.FirstName).Trim(),
                NameEn = (m.Swimmer.LastNameEn + " " + m.Swimmer.FirstNameEn).Trim(),
                m.Swimmer.Gender,
            })
            .ToListAsync();
        var rosterAt = roster.Select((r, i) => (r, i)).ToDictionary(x => x.r.SwimmerId, x => x);

        var swimmerLevels = await _db.HubGroupSwimmerLevels.AsNoTracking()
            .Where(l => l.HubGroupId == hubGroupId)
            .ToDictionaryAsync(l => l.SwimmerId, l => l.LevelId);
        var accountLevels = await _db.HubGroupAccountLevels.AsNoTracking()
            .Where(l => l.HubGroupId == hubGroupId)
            .ToDictionaryAsync(l => l.UserId, l => l.LevelId);
        var levels = await _db.HubGroupLevels.AsNoTracking()
            .Where(l => l.HubGroupId == hubGroupId)
            .OrderBy(l => l.Rank).ThenBy(l => l.Id)
            .Select(l => new TrainingLaneView.LevelInfo(l.Id, l.Rank, l.Name, l.Color))
            .ToListAsync();

        // В бассейне — ответившие «иду» и «не уверен». Пловцы — в порядке состава; аккаунты без
        // пловца — после состава, по имени (детерминированно, не «каждый раз по-разному»).
        var coming = answers.Values
            .Where(a => a.Answer is TrainingRsvpAnswer.Yes or TrainingRsvpAnswer.Maybe && members.ContainsKey(a.UserId))
            .ToList();
        var accountsOrder = coming
            .Where(a => resolved[a.UserId] == null)
            .OrderBy(a => members[a.UserId].Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.UserId)
            .Select((a, i) => (a.UserId, i))
            .ToDictionary(x => x.UserId, x => roster.Count + x.i);

        var pool = coming.Select(a =>
        {
            var member = members[a.UserId];
            if (resolved[a.UserId] is int sid && rosterAt.TryGetValue(sid, out var at))
            {
                return new TrainingLaneView.Person(
                    a.UserId, sid,
                    string.IsNullOrEmpty(at.r.Name) ? at.r.NameEn : at.r.Name,
                    at.r.Gender ?? member.Gender,
                    a.Answer, a.Note,
                    swimmerLevels.TryGetValue(sid, out var sl) ? sl : accountLevels.TryGetValue(a.UserId, out var al) ? al : null,
                    at.i, claims.GetValueOrDefault(sid, 1));
            }
            return new TrainingLaneView.Person(
                a.UserId, null, member.Name, member.Gender, a.Answer, a.Note,
                accountLevels.TryGetValue(a.UserId, out var level) ? level : null,
                accountsOrder[a.UserId], 1);
        }).ToList();

        var planModel = plan == null ? null : new TrainingLaneView.Plan(
            plan.LaneCount,
            plan.Lanes.Select(l => new TrainingLaneView.PlanLane(l.LaneNo, l.LevelId, l.Workout)).ToList(),
            plan.Swimmers.ToDictionary(s => s.SwimmerId, s => (s.LaneNo, s.OrderNo)));

        var namesVisible = isManager || schedule.EffectiveWhoIsComing == GroupWhoIsComing.Members;
        return TrainingLaneView.Build(mode, planModel, schedule.UsualLanes, lastPlanLanes, levels, pool,
            viewerUserId, namesVisible);
    }

    private Task<bool> IsActiveMemberAsync(int hubGroupId, int userId) =>
        _db.HubGroupUserMembers.AnyAsync(m =>
            m.HubGroupId == hubGroupId && m.UserId == userId && m.Status == HubGroupUserMemberStatus.Active);

    /// <summary>Порядок групп списка у управляющего: иду → не уверен → не приду → без ответа → на перерыве.</summary>
    private static int AnswerOrder(string? answer, bool onBreak) => answer switch
    {
        TrainingRsvpAnswer.Yes => 0,
        TrainingRsvpAnswer.Maybe => 1,
        TrainingRsvpAnswer.No => 2,
        _ => onBreak ? 4 : 3,
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

        // На перерыве в день занятия (Ш3.1): не ответил — не в знаменателе и не в «нет ответа»;
        // ответил — считается как все (сумма иду + не уверен + не приду + нет ответа = total).
        var onBreak = await HubGroupBreakQuery.LoadAsync(_db, hubGroupId, date);
        var counted = memberIds.Count(id => !onBreak.UserIds.Contains(id) || answers.ContainsKey(id));
        var viewerBreakUntil = !onBreak.UserIds.Contains(viewerUserId) ? null : await _db.HubGroupBreaks.AsNoTracking()
            .Where(b => b.HubGroupId == hubGroupId && b.UserId == viewerUserId && b.EndedAt == null)
            .Select(b => b.Until)
            .FirstOrDefaultAsync();

        var backAfter = new Dictionary<int, int>();
        if (isManager)
        {
            var nowUtc = DateTime.UtcNow;
            var markerFrom = nowUtc.AddDays(-HubGroupBreakRules.BackMarkerDays);
            var returns = await _db.HubGroupBreaks.AsNoTracking()
                .Where(b => b.HubGroupId == hubGroupId && b.UserId != null && b.EndedByRsvp && b.EndedAt >= markerFrom)
                .Select(b => new { UserId = b.UserId!.Value, b.Since, b.EndedAt, b.EndedByRsvp })
                .ToListAsync();
            foreach (var r in returns)
                if (HubGroupBreakRules.BackAfterDays(r.Since, r.EndedAt, r.EndedByRsvp, nowUtc) is int days)
                    backAfter[r.UserId] = Math.Max(backAfter.GetValueOrDefault(r.UserId), days);
        }

        var slot = TrainingRsvpRules.FindSlot(schedule, date, start)!;
        var isMember = memberIds.Contains(viewerUserId);
        answers.TryGetValue(viewerUserId, out var mine);

        var laneView = await BuildLaneViewAsync(hubGroupId, schedule, date,
            members.ToDictionary(m => m.UserId, m => (m.Name, m.Gender)), answers, viewerUserId, isManager);

        return new TrainingRsvpDto
        {
            SessionId = TrainingRsvpRules.SessionKey(date, start),
            Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Start = start,
            End = slot.End,
            Yes = answers.Values.Count(r => r.Answer == TrainingRsvpAnswer.Yes),
            Maybe = answers.Values.Count(r => r.Answer == TrainingRsvpAnswer.Maybe),
            No = answers.Values.Count(r => r.Answer == TrainingRsvpAnswer.No),
            Total = counted,
            Mine = mine == null ? null : new TrainingRsvpMineDto
            {
                Answer = mine.Answer, Note = mine.Note, SetByCoach = mine.SetByUserId != null,
            },
            IsMember = isMember,
            CanManage = isManager,
            CanAnswer = (isMember || isManager)
                        && TrainingRsvpRules.EditBlockReason(schedule, date, start, now, isManager) == null,
            OnBreak = onBreak.UserIds.Contains(viewerUserId),
            BreakUntil = viewerBreakUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            LaneView = laneView,
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
                        OnBreak = onBreak.UserIds.Contains(m.UserId),
                        BackAfterDays = backAfter.TryGetValue(m.UserId, out var days) ? days : null,
                    };
                })
                .OrderBy(p => AnswerOrder(p.Answer, p.OnBreak))
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
        };
    }
}
