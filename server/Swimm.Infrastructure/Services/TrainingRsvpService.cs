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

    private static DateTime DayStart(DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    /// <summary>
    /// Разовые занятия группы (тренировки с «Ask who's coming» и заданным началом) за даты
    /// [<paramref name="from"/>; <paramref name="to"/>]. Дата тренировки в базе — полночь UTC
    /// (<c>Sys_TrainingSessions.Date</c> — timestamptz), поэтому границы берём по дню.
    /// </summary>
    private async Task<List<TrainingOccurrences.OneOff>> OneOffsAsync(int hubGroupId, DateOnly from, DateOnly to)
    {
        var fromDb = DayStart(from);
        var toDb = DayStart(to.AddDays(1));
        var rows = await _db.TrainingSessions.AsNoTracking()
            .Where(s => s.HubGroupId == hubGroupId && s.AsksRsvp && s.StartTime != null
                        && s.Date >= fromDb && s.Date < toDb)
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.Date, s.StartTime, s.Title, s.Type, s.PoolType })
            .ToListAsync();
        return rows.Select(r => new TrainingOccurrences.OneOff(
                r.Id, DateOnly.FromDateTime(r.Date), r.StartTime!, r.Title, r.Type,
                string.IsNullOrWhiteSpace(r.PoolType) ? null : r.PoolType))
            .ToList();
    }

    private async Task<List<TrainingOccurrences.Cancellation>> CancellationsAsync(
        int hubGroupId, DateOnly from, DateOnly to)
    {
        var rows = await _db.HubGroupTrainingCancellations.AsNoTracking()
            .Where(c => c.HubGroupId == hubGroupId && c.Date >= from && c.Date <= to)
            .Select(c => new { c.Date, c.Start, c.Reason })
            .ToListAsync();
        return rows.Select(r => new TrainingOccurrences.Cancellation(r.Date, r.Start, r.Reason)).ToList();
    }

    /// <summary>Занятие с этим ключом (слот расписания или разовое) вместе с пометкой «отменено»; null — такого нет.</summary>
    private async Task<TrainingOccurrences.Occurrence?> FindOccurrenceAsync(
        int hubGroupId, GroupTrainingSchedule schedule, DateOnly date, string start)
    {
        var oneOffs = await OneOffsAsync(hubGroupId, date, date);
        var cancellations = await CancellationsAsync(hubGroupId, date, date);
        return TrainingOccurrences.Find(schedule, oneOffs, cancellations, date, start);
    }

    public async Task<TrainingRsvpDto?> GetAsync(int hubGroupId, string sessionKey, int viewerUserId, bool isManager,
        DateTime? nowLocal = null)
    {
        if (!TrainingRsvpRules.TryParseSessionKey(sessionKey, out var date, out var start)) return null;
        var schedule = await ScheduleAsync(hubGroupId);
        var now = Now(nowLocal);
        if (schedule == null) return null;
        var occurrence = await FindOccurrenceAsync(hubGroupId, schedule, date, start);
        if (!TrainingRsvpRules.IsViewable(occurrence, now)) return null;

        return await BuildAsync(hubGroupId, schedule, occurrence!, viewerUserId, isManager, now);
    }

    public async Task<List<NextTrainingDto>?> GetNextAsync(int hubGroupId, DateTime? nowLocal = null)
    {
        var schedule = await ScheduleAsync(hubGroupId);
        if (schedule == null) return null;

        var now = Now(nowLocal);
        var today = DateOnly.FromDateTime(now);
        var last = today.AddDays(TrainingRsvpRules.DaysAhead);
        var oneOffs = await OneOffsAsync(hubGroupId, today, last);
        var cancellations = await CancellationsAsync(hubGroupId, today, last);

        return TrainingOccurrences.Upcoming(schedule, oneOffs, cancellations, now, TrainingRsvpRules.NextCount)
            .Select(o => new NextTrainingDto
            {
                Id = TrainingRsvpRules.SessionKey(o.Date, o.Start),
                Date = o.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Start = o.Start,
                End = o.End,
                Place = schedule.Place,
                PoolType = o.PoolType ?? schedule.PoolType,
                Title = o.Title,
                Type = o.Type,
                TrainingId = o.TrainingId,
                OneOff = o.OneOff,
                Cancelled = o.Cancelled,
                CancelReason = o.CancelReason,
            })
            .ToList();
    }

    public async Task<TrainingRsvpSaveResult> CancelAsync(int hubGroupId, string sessionKey, int actorUserId,
        string? reason, DateTime? nowLocal = null)
    {
        if (!TrainingRsvpRules.TryParseSessionKey(sessionKey, out var date, out var start))
            return TrainingRsvpSaveResult.Fail(404, "Unknown training.");
        var schedule = await ScheduleAsync(hubGroupId);
        if (schedule == null) return TrainingRsvpSaveResult.Fail(404, "Group not found.");

        var now = Now(nowLocal);
        var occurrence = await FindOccurrenceAsync(hubGroupId, schedule, date, start);
        if (!TrainingRsvpRules.IsViewable(occurrence, now))
            return TrainingRsvpSaveResult.Fail(400, "There is no such training.");

        var text = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (text?.Length > TrainingRsvpRules.CancelReasonMax)
            return TrainingRsvpSaveResult.Fail(400,
                $"Reason is longer than {TrainingRsvpRules.CancelReasonMax} characters.");

        var row = await _db.HubGroupTrainingCancellations.FirstOrDefaultAsync(c =>
            c.HubGroupId == hubGroupId && c.Date == date && c.Start == start);
        if (row == null)
        {
            row = new HubGroupTrainingCancellation
            {
                HubGroupId = hubGroupId, Date = date, Start = start, Reason = text,
                CreatedByUserId = actorUserId, CreatedAt = DateTime.UtcNow,
            };
            _db.HubGroupTrainingCancellations.Add(row);
        }
        else
        {
            row.Reason = text;   // повторная отмена — успех, причина обновляется
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Две отмены одного занятия одновременно: вторая упёрлась в UNIQUE (группа, дата, начало).
            // Занятие уже отменено — обновляем причину у победившей строки.
            _db.Entry(row).State = EntityState.Detached;
            var winner = await _db.HubGroupTrainingCancellations.FirstOrDefaultAsync(c =>
                c.HubGroupId == hubGroupId && c.Date == date && c.Start == start);
            if (winner == null) throw;
            winner.Reason = text;
            await _db.SaveChangesAsync();
        }

        return await OkAsync(hubGroupId, schedule, date, start, actorUserId, now);
    }

    public async Task<TrainingRsvpSaveResult> RestoreAsync(int hubGroupId, string sessionKey, int actorUserId,
        DateTime? nowLocal = null)
    {
        if (!TrainingRsvpRules.TryParseSessionKey(sessionKey, out var date, out var start))
            return TrainingRsvpSaveResult.Fail(404, "Unknown training.");
        var schedule = await ScheduleAsync(hubGroupId);
        if (schedule == null) return TrainingRsvpSaveResult.Fail(404, "Group not found.");

        var now = Now(nowLocal);
        var occurrence = await FindOccurrenceAsync(hubGroupId, schedule, date, start);
        if (!TrainingRsvpRules.IsViewable(occurrence, now))
            return TrainingRsvpSaveResult.Fail(400, "There is no such training.");

        var row = await _db.HubGroupTrainingCancellations.FirstOrDefaultAsync(c =>
            c.HubGroupId == hubGroupId && c.Date == date && c.Start == start);
        if (row != null)
        {
            _db.HubGroupTrainingCancellations.Remove(row);
            await _db.SaveChangesAsync();
        }

        return await OkAsync(hubGroupId, schedule, date, start, actorUserId, now);
    }

    /// <summary>Ответы занятия после отмены / возврата — как GET, глазами управляющего.</summary>
    private async Task<TrainingRsvpSaveResult> OkAsync(int hubGroupId, GroupTrainingSchedule schedule,
        DateOnly date, string start, int actorUserId, DateTime now)
    {
        var occurrence = await FindOccurrenceAsync(hubGroupId, schedule, date, start);
        return TrainingRsvpSaveResult.Ok(
            await BuildAsync(hubGroupId, schedule, occurrence!, actorUserId, isManager: true, now));
    }

    public async Task<TrainingRsvpSaveResult> SetAsync(int hubGroupId, string sessionKey, int actorUserId,
        bool isManager, TrainingRsvpInputDto input, DateTime? nowLocal = null)
    {
        if (!TrainingRsvpRules.TryParseSessionKey(sessionKey, out var date, out var start))
            return TrainingRsvpSaveResult.Fail(404, "Unknown training.");
        var schedule = await ScheduleAsync(hubGroupId);
        if (schedule == null) return TrainingRsvpSaveResult.Fail(404, "Group not found.");

        var now = Now(nowLocal);
        var occurrence = await FindOccurrenceAsync(hubGroupId, schedule, date, start);
        var blocked = TrainingRsvpRules.EditBlockReason(occurrence, now, isManager);
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

        if (input.UserId != null && input.SwimmerId != null)
            return TrainingRsvpSaveResult.Fail(400, "Answer for a member or for a swimmer, not both.");

        int? targetUserId = null, targetSwimmerId = null;
        bool forOther;
        if (input.SwimmerId is int swimmerId)
        {
            // Пловец группы без аккаунта (Р73): сам ответить не может — только тренер. Стоит им
            // активный участник — отвечает аккаунт, иначе человек посчитался бы дважды.
            if (!isManager)
                return TrainingRsvpSaveResult.Fail(403, "Only the group coach can answer for a swimmer.");
            var block = await GroupSwimmerBlockReasonAsync(hubGroupId, swimmerId);
            if (block != null) return TrainingRsvpSaveResult.Fail(400, block);
            targetSwimmerId = swimmerId;
            forOther = true;
        }
        else
        {
            // За другого отвечает только управляющий (тренер по просьбе в WhatsApp), и только за
            // активного участника. Сам за себя — только участник: управляющий вне состава в
            // полосе не считается, и его ответ повис бы невидимым.
            targetUserId = input.UserId ?? actorUserId;
            forOther = targetUserId != actorUserId;
            if (forOther && !isManager)
                return TrainingRsvpSaveResult.Fail(403, "Only the group coach can answer for another member.");

            var targetIsMember = await IsActiveMemberAsync(hubGroupId, targetUserId.Value);
            if (!targetIsMember)
                return TrainingRsvpSaveResult.Fail(400, forOther
                    ? "This person is not an active member of the group."
                    : "Only group members answer. Join the group first.");
        }

        var row = await _db.HubGroupTrainingRsvps.FirstOrDefaultAsync(r =>
            r.HubGroupId == hubGroupId && r.SessionDate == date && r.SessionStart == start
            && (targetSwimmerId != null ? r.SwimmerId == targetSwimmerId : r.UserId == targetUserId));

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
                    HubGroupId = hubGroupId, SessionDate = date, SessionStart = start,
                    UserId = targetUserId, SwimmerId = targetSwimmerId,
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
        if (answer == TrainingRsvpAnswer.Yes) await EndBreaksByRsvpAsync(hubGroupId, targetUserId, targetSwimmerId, date);

        await _db.SaveChangesAsync();

        return TrainingRsvpSaveResult.Ok(await BuildAsync(hubGroupId, schedule, occurrence!, actorUserId, isManager, now));
    }

    /// <summary>
    /// Закрыть открытые перерывы человека на дату занятия: его аккаунта и пловца, которым он стоит
    /// в группе (флаг один на человека — <see cref="HubGroupBreakQuery"/>, <see cref="HubGroupPersonResolver"/>).
    /// Ответ за пловца группы (Р73) закрывает перерыв самого пловца.
    /// </summary>
    private async Task EndBreaksByRsvpAsync(int hubGroupId, int? userId, int? swimmerId, DateOnly sessionDate)
    {
        var labelSwimmer = swimmerId
            ?? (await HubGroupPersonResolver.ResolveAsync(_db, hubGroupId, [userId!.Value]))[userId.Value];

        // Только перерыв: «не тренируется с группой» (Р74) ответ не снимает — человек посчитан
        // на этот день, и всё.
        var open = await _db.HubGroupBreaks
            .Where(b => b.HubGroupId == hubGroupId && b.EndedAt == null && b.Kind == HubGroupBreakKind.Break
                        && (b.Until == null || b.Until >= sessionDate)
                        && ((userId != null && b.UserId == userId) || (labelSwimmer != null && b.SwimmerId == labelSwimmer)))
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
        IReadOnlyDictionary<int, int?> resolved, IReadOnlyDictionary<int, HubGroupTrainingRsvp> answers,
        IReadOnlyDictionary<int, HubGroupTrainingRsvp> swimmerAnswers, int viewerUserId, bool isManager)
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
        static bool InPool(HubGroupTrainingRsvp a) => a.Answer is TrainingRsvpAnswer.Yes or TrainingRsvpAnswer.Maybe;
        var coming = answers.Values.Where(a => InPool(a) && members.ContainsKey(a.UserId!.Value)).ToList();
        var accountsOrder = coming
            .Where(a => resolved[a.UserId!.Value] == null)
            .OrderBy(a => members[a.UserId!.Value].Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.UserId)
            .Select((a, i) => (UserId: a.UserId!.Value, i))
            .ToDictionary(x => x.UserId, x => roster.Count + x.i);

        var pool = coming.Select(a =>
        {
            var userId = a.UserId!.Value;
            var member = members[userId];
            if (resolved[userId] is int sid && rosterAt.TryGetValue(sid, out var at))
            {
                return new TrainingLaneView.Person(
                    userId, sid,
                    string.IsNullOrEmpty(at.r.Name) ? at.r.NameEn : at.r.Name,
                    at.r.Gender ?? member.Gender,
                    a.Answer, a.Note,
                    swimmerLevels.TryGetValue(sid, out var sl) ? sl : accountLevels.TryGetValue(userId, out var al) ? al : null,
                    at.i, claims.GetValueOrDefault(sid, 1));
            }
            return new TrainingLaneView.Person(
                userId, null, member.Name, member.Gender, a.Answer, a.Note,
                accountLevels.TryGetValue(userId, out var level) ? level : null,
                accountsOrder[userId], 1);
        }).ToList();

        // Пловцы группы без аккаунта (Р73): за них ответил тренер, стоят пловцом состава.
        pool.AddRange(swimmerAnswers.Values
            .Where(a => InPool(a) && rosterAt.ContainsKey(a.SwimmerId!.Value))
            .Select(a =>
            {
                var sid = a.SwimmerId!.Value;
                var at = rosterAt[sid];
                return new TrainingLaneView.Person(
                    null, sid,
                    string.IsNullOrEmpty(at.r.Name) ? at.r.NameEn : at.r.Name,
                    at.r.Gender, a.Answer, a.Note,
                    swimmerLevels.TryGetValue(sid, out var sl) ? sl : null,
                    at.i, 1);
            }));

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

    private sealed record GroupSwimmer(int SwimmerId, string Name, string? Gender);

    /// <summary>
    /// Пловцы группы без аккаунта, за которых отвечает тренер (Р73): <c>Swimmer.PrivateHubGroupId</c>
    /// этой группы, в составе и не неактивны. Кем-то из <paramref name="claimed"/> (пловцы, которыми
    /// стоят активные участники, <see cref="HubGroupPersonResolver"/>) — нет: человек отвечает
    /// аккаунтом и дважды не считается. Имя — как в виде по дорожкам: иврит, иначе английское.
    /// </summary>
    private async Task<List<GroupSwimmer>> LoadGroupSwimmersAsync(int hubGroupId, IReadOnlySet<int> claimed)
    {
        var rows = await _db.HubGroupMembers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && !m.IsExcluded && m.Swimmer!.PrivateHubGroupId == hubGroupId)
            .Select(m => new
            {
                m.SwimmerId,
                Name = (m.Swimmer!.LastName + " " + m.Swimmer.FirstName).Trim(),
                NameEn = (m.Swimmer.LastNameEn + " " + m.Swimmer.FirstNameEn).Trim(),
                m.Swimmer.Gender,
            })
            .ToListAsync();
        return rows
            .Where(r => !claimed.Contains(r.SwimmerId))
            .Select(r => new GroupSwimmer(r.SwimmerId, string.IsNullOrEmpty(r.Name) ? r.NameEn : r.Name, r.Gender))
            .ToList();
    }

    /// <summary>Почему за этого пловца нельзя ответить (Р73); null — можно.</summary>
    private async Task<string?> GroupSwimmerBlockReasonAsync(int hubGroupId, int swimmerId)
    {
        var inGroup = await _db.HubGroupMembers.AnyAsync(m =>
            m.HubGroupId == hubGroupId && m.SwimmerId == swimmerId && !m.IsExcluded
            && m.Swimmer!.PrivateHubGroupId == hubGroupId);
        if (!inGroup) return "This swimmer is not an active swimmer of the group.";

        var memberIds = await ActiveMemberIdsAsync(hubGroupId);
        var resolved = await HubGroupPersonResolver.ResolveAsync(_db, hubGroupId, memberIds);
        return resolved.Values.Contains(swimmerId)
            ? "A member account stands for this swimmer — answer for that member instead."
            : null;
    }

    private Task<List<int>> ActiveMemberIdsAsync(int hubGroupId) =>
        _db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && m.Status == HubGroupUserMemberStatus.Active)
            .Select(m => m.UserId)
            .ToListAsync();

    /// <summary>
    /// Порядок групп списка у управляющего: иду → не уверен → не приду → без ответа → на перерыве
    /// → не тренируется с группой (Р74).
    /// </summary>
    private static int AnswerOrder(string? answer, bool onBreak, bool notTraining) => answer switch
    {
        TrainingRsvpAnswer.Yes => 0,
        TrainingRsvpAnswer.Maybe => 1,
        TrainingRsvpAnswer.No => 2,
        _ => notTraining ? 5 : onBreak ? 4 : 3,
    };

    private async Task<TrainingRsvpDto> BuildAsync(int hubGroupId, GroupTrainingSchedule schedule,
        TrainingOccurrences.Occurrence occurrence, int viewerUserId, bool isManager, DateTime now)
    {
        var date = occurrence.Date;
        var start = occurrence.Start;
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

        // Пловцы группы без аккаунта (Р73) — в том же знаменателе, кроме тех, кем стоит участник.
        var resolved = await HubGroupPersonResolver.ResolveAsync(_db, hubGroupId, memberIds.ToList());
        var groupSwimmers = await LoadGroupSwimmersAsync(hubGroupId,
            resolved.Values.Where(v => v != null).Select(v => v!.Value).ToHashSet());
        var groupSwimmerIds = groupSwimmers.Select(s => s.SwimmerId).ToHashSet();

        var rows = await _db.HubGroupTrainingRsvps.AsNoTracking()
            .Where(r => r.HubGroupId == hubGroupId && r.SessionDate == date && r.SessionStart == start)
            .ToListAsync();
        var answers = rows
            .Where(r => r.UserId is int u && memberIds.Contains(u))
            .ToDictionary(r => r.UserId!.Value);
        var swimmerAnswers = rows
            .Where(r => r.SwimmerId is int s && groupSwimmerIds.Contains(s))
            .ToDictionary(r => r.SwimmerId!.Value);
        var all = answers.Values.Concat(swimmerAnswers.Values).ToList();

        // На перерыве в день занятия (Ш3.1): не ответил — не в знаменателе и не в «нет ответа»;
        // ответил — считается как все (сумма иду + не уверен + не приду + нет ответа = total).
        var onBreak = await HubGroupBreakQuery.LoadAsync(_db, hubGroupId, date);
        var counted = memberIds.Count(id => !onBreak.UserIds.Contains(id) || answers.ContainsKey(id))
                      + groupSwimmerIds.Count(id => !onBreak.SwimmerIds.Contains(id) || swimmerAnswers.ContainsKey(id));
        var viewerBreakUntil = !onBreak.UserIds.Contains(viewerUserId) ? null : await _db.HubGroupBreaks.AsNoTracking()
            .Where(b => b.HubGroupId == hubGroupId && b.UserId == viewerUserId && b.EndedAt == null)
            .Select(b => b.Until)
            .FirstOrDefaultAsync();

        var backAfter = new Dictionary<int, int>();
        var swimmerBackAfter = new Dictionary<int, int>();
        if (isManager)
        {
            var nowUtc = DateTime.UtcNow;
            var markerFrom = nowUtc.AddDays(-HubGroupBreakRules.BackMarkerDays);
            var returns = await _db.HubGroupBreaks.AsNoTracking()
                .Where(b => b.HubGroupId == hubGroupId && b.EndedByRsvp && b.EndedAt >= markerFrom)
                .Select(b => new { b.UserId, b.SwimmerId, b.Since, b.EndedAt, b.EndedByRsvp })
                .ToListAsync();
            foreach (var r in returns)
            {
                if (HubGroupBreakRules.BackAfterDays(r.Since, r.EndedAt, r.EndedByRsvp, nowUtc) is not int days) continue;
                var (target, key) = r.UserId is int u ? (backAfter, u) : (swimmerBackAfter, r.SwimmerId!.Value);
                target[key] = Math.Max(target.GetValueOrDefault(key), days);
            }
        }

        var isMember = memberIds.Contains(viewerUserId);
        answers.TryGetValue(viewerUserId, out var mine);

        var laneView = await BuildLaneViewAsync(hubGroupId, schedule, date,
            members.ToDictionary(m => m.UserId, m => (m.Name, m.Gender)), resolved, answers, swimmerAnswers,
            viewerUserId, isManager);

        return new TrainingRsvpDto
        {
            SessionId = TrainingRsvpRules.SessionKey(date, start),
            Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Start = start,
            End = occurrence.End,
            Cancelled = occurrence.Cancelled,
            CancelReason = occurrence.CancelReason,
            Title = occurrence.Title,
            Type = occurrence.Type,
            Yes = all.Count(r => r.Answer == TrainingRsvpAnswer.Yes),
            Maybe = all.Count(r => r.Answer == TrainingRsvpAnswer.Maybe),
            No = all.Count(r => r.Answer == TrainingRsvpAnswer.No),
            Total = counted,
            Mine = mine == null ? null : new TrainingRsvpMineDto
            {
                Answer = mine.Answer, Note = mine.Note, SetByCoach = mine.SetByUserId != null,
            },
            IsMember = isMember,
            CanManage = isManager,
            CanAnswer = (isMember || isManager)
                        && TrainingRsvpRules.EditBlockReason(occurrence, now, isManager) == null,
            OnBreak = onBreak.UserIds.Contains(viewerUserId) && !onBreak.NotTrainingUserIds.Contains(viewerUserId),
            NotTraining = onBreak.NotTrainingUserIds.Contains(viewerUserId),
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
                        OnBreak = onBreak.UserIds.Contains(m.UserId) && !onBreak.NotTrainingUserIds.Contains(m.UserId),
                        NotTraining = onBreak.NotTrainingUserIds.Contains(m.UserId),
                        BackAfterDays = backAfter.TryGetValue(m.UserId, out var days) ? days : null,
                    };
                })
                .Concat(groupSwimmers.Select(s =>
                {
                    swimmerAnswers.TryGetValue(s.SwimmerId, out var a);
                    return new TrainingRsvpPersonDto
                    {
                        SwimmerId = s.SwimmerId,
                        Name = s.Name,
                        Gender = s.Gender is "male" or "female" ? s.Gender : null,
                        Answer = a?.Answer,
                        Note = a?.Note,
                        // За пловца без аккаунта всегда отвечает тренер — пометка «by coach» тут шум.
                        SetByCoach = false,
                        OnBreak = onBreak.SwimmerIds.Contains(s.SwimmerId) && !onBreak.NotTrainingSwimmerIds.Contains(s.SwimmerId),
                        NotTraining = onBreak.NotTrainingSwimmerIds.Contains(s.SwimmerId),
                        BackAfterDays = swimmerBackAfter.TryGetValue(s.SwimmerId, out var days) ? days : null,
                    };
                }))
                .OrderBy(p => AnswerOrder(p.Answer, p.OnBreak, p.NotTraining))
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
        };
    }
}
