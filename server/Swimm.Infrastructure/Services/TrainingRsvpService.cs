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

    private Task<List<TrainingOccurrences.OneOff>> OneOffsAsync(int hubGroupId, DateOnly from, DateOnly to) =>
        TrainingOccurrenceQuery.OneOffsAsync(_db, hubGroupId, from, to);

    private Task<List<TrainingOccurrences.Cancellation>> CancellationsAsync(int hubGroupId, DateOnly from, DateOnly to) =>
        TrainingOccurrenceQuery.CancellationsAsync(_db, hubGroupId, from, to);

    /// <summary>Занятие с этим ключом (слот расписания или разовое) вместе с пометкой «отменено»; null — такого нет.</summary>
    private Task<TrainingOccurrences.Occurrence?> FindOccurrenceAsync(
        int hubGroupId, GroupTrainingSchedule schedule, DateOnly date, string start) =>
        TrainingOccurrenceQuery.FindAsync(_db, hubGroupId, schedule, date, start);

    public async Task<TrainingRsvpDto?> GetAsync(int hubGroupId, string sessionKey, int viewerUserId, bool isManager,
        DateTime? nowLocal = null)
    {
        if (!TrainingRsvpRules.TryParseSessionKey(sessionKey, out var date, out var start)) return null;
        var schedule = await ScheduleAsync(hubGroupId);
        var now = Now(nowLocal);
        if (schedule == null) return null;
        var occurrence = await FindOccurrenceAsync(hubGroupId, schedule, date, start);
        if (!TrainingRsvpRules.IsViewable(occurrence, now, isManager)) return null;

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
            .Select(o => ToDto(o, schedule))
            .ToList();
    }

    private static NextTrainingDto ToDto(TrainingOccurrences.Occurrence o, GroupTrainingSchedule schedule) => new()
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
    };

    public async Task<List<PastTrainingDto>?> GetPastAsync(int hubGroupId, DateTime? nowLocal = null)
    {
        var schedule = await ScheduleAsync(hubGroupId);
        if (schedule == null) return null;

        var now = Now(nowLocal);
        var today = DateOnly.FromDateTime(now);
        var first = today.AddDays(-TrainingRsvpRules.PastDaysBack);
        var oneOffs = await OneOffsAsync(hubGroupId, first, today);
        var cancellations = await CancellationsAsync(hubGroupId, first, today);

        // Ответы — счётчики по занятию сразу (ключ занятия — дата + начало).
        var answers = await _db.HubGroupTrainingRsvps.AsNoTracking()
            .Where(r => r.HubGroupId == hubGroupId && r.SessionDate >= first && r.SessionDate <= today)
            .GroupBy(r => new { r.SessionDate, r.SessionStart, r.Answer })
            .Select(g => new { g.Key.SessionDate, g.Key.SessionStart, g.Key.Answer, Count = g.Count() })
            .ToListAsync();
        // План — на занятие (CF5): его ключ — такой же, как у ответов. План дня без занятия (архив,
        // Start null) даёт все занятия своей даты, как до CF5.
        var plans = await _db.LanePlans.AsNoTracking()
            .Where(p => p.HubGroupId == hubGroupId && p.Date >= first && p.Date <= today)
            .Select(p => new { p.Date, p.Start })
            .ToListAsync();
        var sessionKeys = answers.Select(a => (a.SessionDate, a.SessionStart))
            .Concat(plans.Where(p => p.Start != null).Select(p => (p.Date, p.Start!)));

        int Count(DateOnly date, string start, string answer) =>
            answers.Where(a => a.SessionDate == date && a.SessionStart == start && a.Answer == answer).Sum(a => a.Count);

        return TrainingOccurrences.Past(schedule, oneOffs, cancellations,
                sessionKeys, plans.Where(p => p.Start == null).Select(p => p.Date), now)
            .Select(o => new PastTrainingDto
            {
                Session = ToDto(o, schedule),
                Yes = Count(o.Date, o.Start, "yes"),
                Maybe = Count(o.Date, o.Start, "maybe"),
                No = Count(o.Date, o.Start, "no"),
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

        // Ответ за аккаунт заменяет ответ тренера за его пловца на это занятие
        // (TrainingAnswerFallback): иначе пловец получил бы два ответа, и снятый ответ аккаунта
        // «воскрешал» бы старый ответ тренера.
        if (targetUserId is int answeredUser)
            await DropSwimmerShadowAsync(hubGroupId, answeredUser, date, start);

        // «Going» на занятие внутри перерыва — человек вернулся (Ш3.1): перерыв закрывается,
        // тренеру — пометка «back after …». Перерыв, который к этой дате и так кончится, не трогаем.
        if (answer == TrainingRsvpAnswer.Yes) await EndBreaksByRsvpAsync(hubGroupId, targetUserId, targetSwimmerId, date);

        await _db.SaveChangesAsync();

        var laneChange = await MoveInLanePlanAsync(hubGroupId, LanePlanKey.Session(date, start), targetUserId, targetSwimmerId, answer,
            isManager ? input.RestoreLane : null, actorUserId, forOther);

        var dto = await BuildAsync(hubGroupId, schedule, occurrence!, actorUserId, isManager, now);
        dto.LaneChange = laneChange;
        return TrainingRsvpSaveResult.Ok(dto);
    }

    /// <summary>
    /// Ответ двигает пловца в плане дорожек ЭТОГО занятия (§5.3 плана trainings-coach-flow, CF4; с CF5 план —
    /// у занятия, а не у даты): «Going» —
    /// на дорожку его уровня, «Can't come» — в «Not today»; «Not sure» и снятый ответ план не
    /// трогают. <paramref name="restore"/> (Undo тоста тренера) — вернуть на прежнее место вместо
    /// правила. Аккаунт, который никем из состава не стоит, в плане не бывает.
    /// </summary>
    private async Task<LaneChangeDto?> MoveInLanePlanAsync(int hubGroupId, LanePlanKey plan, int? userId, int? swimmerId,
        string? answer, LanePlaceDto? restore, int actorUserId, bool byCoach)
    {
        if (restore == null && answer is not (TrainingRsvpAnswer.Yes or TrainingRsvpAnswer.No)) return null;
        var target = swimmerId
            ?? (await HubGroupPersonResolver.ResolveAsync(_db, hubGroupId, [userId!.Value]))[userId.Value];
        if (target is not int sid) return null;

        if (restore != null) return await LanePlanPlacementStore.RestoreAsync(_db, hubGroupId, plan, sid, restore);
        var going = answer == TrainingRsvpAnswer.Yes;
        // Строка ленты «Since publishing» (CF8): кто ответил и ответил ли тренер за человека.
        return await LanePlanPlacementStore.ApplyAsync(_db, hubGroupId, plan, sid,
            going ? LanePlanPlacementStore.Intent.Going : LanePlanPlacementStore.Intent.Out,
            new LanePlanEventLog.Source(going ? LanePlanEventKind.Going : LanePlanEventKind.CantCome, actorUserId, byCoach));
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
        DateOnly date, string start, IReadOnlyDictionary<int, (string Name, string? Gender)> members,
        IReadOnlyDictionary<int, int?> resolved, IReadOnlyDictionary<int, HubGroupTrainingRsvp> answers,
        IReadOnlyDictionary<int, HubGroupTrainingRsvp> swimmerAnswers, int viewerUserId, bool isManager)
    {
        var mode = schedule.EffectiveLaneView;
        if (mode == GroupLaneView.Off) return null;

        var plan = await _db.LanePlans.AsNoTracking()
            .Include(p => p.Lanes)
            .Include(p => p.Swimmers)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.HubGroupId == hubGroupId && p.Date == date && p.Start == start
                                       && p.Status == LanePlanStatus.Published);
        if (plan == null && mode == GroupLaneView.Plan) return null;
        var lastPlanLanes = plan != null || schedule.UsualLanes != null ? null : await _db.LanePlans.AsNoTracking()
            .Where(p => p.HubGroupId == hubGroupId)
            .OrderByDescending(p => p.Date).ThenByDescending(p => p.Start)
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
        // Ключ — аккаунт, а не `UserId` строки: ответ тренера за пловца аккаунта лежит строкой пловца
        // (TrainingAnswerFallback).
        var coming = answers.Where(kv => InPool(kv.Value) && members.ContainsKey(kv.Key)).ToList();
        var accountsOrder = coming
            .Where(kv => resolved[kv.Key] == null)
            .OrderBy(kv => members[kv.Key].Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(kv => kv.Key)
            .Select((kv, i) => (UserId: kv.Key, i))
            .ToDictionary(x => x.UserId, x => roster.Count + x.i);

        var pool = coming.Select(kv =>
        {
            var (userId, a) = (kv.Key, kv.Value);
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
            plan.Swimmers.ToDictionary(s => s.SwimmerId, s => (s.LaneNo, s.OrderNo)),
            WorkoutRules.Parse(plan.Workout));

        var namesVisible = isManager || schedule.EffectiveWhoIsComing == GroupWhoIsComing.Members;
        return TrainingLaneView.Build(mode, planModel, schedule.UsualLanes, lastPlanLanes, levels, pool,
            viewerUserId, namesVisible, workoutOnEveryLane: isManager);
    }

    /// <summary>Убрать строку ответа за пловца, которым стоит аккаунт, на это занятие.</summary>
    private async Task DropSwimmerShadowAsync(int hubGroupId, int userId, DateOnly date, string start)
    {
        var swimmerOf = await HubGroupPersonResolver.ResolveAsync(_db, hubGroupId, new[] { userId });
        if (swimmerOf[userId] is not int swimmerId) return;
        var shadow = await _db.HubGroupTrainingRsvps.FirstOrDefaultAsync(r =>
            r.HubGroupId == hubGroupId && r.SessionDate == date && r.SessionStart == start && r.SwimmerId == swimmerId);
        if (shadow != null) _db.HubGroupTrainingRsvps.Remove(shadow);
    }

    private Task<bool> IsActiveMemberAsync(int hubGroupId, int userId) =>
        _db.HubGroupUserMembers.AnyAsync(m =>
            m.HubGroupId == hubGroupId && m.UserId == userId && m.Status == HubGroupUserMemberStatus.Active);

    private sealed record GroupSwimmer(int SwimmerId, string Name, string? Gender);

    /// <summary>
    /// Пловцы без аккаунта, за которых отвечает тренер (Р73, расширено решением 14 плана
    /// trainings-coach-flow, CF4): ЛЮБОЙ пловец видимого состава — заведённый в группе
    /// (<c>Swimmer.PrivateHubGroupId</c>) или из справочника федерации. Кем-то из <paramref name="claimed"/> (пловцы, которыми
    /// стоят активные участники, <see cref="HubGroupPersonResolver"/>) — нет: человек отвечает
    /// аккаунтом и дважды не считается. Имя — как в виде по дорожкам: иврит, иначе английское.
    /// </summary>
    private async Task<List<GroupSwimmer>> LoadGroupSwimmersAsync(int hubGroupId, IReadOnlySet<int> claimed)
    {
        var rows = await _db.HubGroupMembers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && !m.IsExcluded)
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
            m.HubGroupId == hubGroupId && m.SwimmerId == swimmerId && !m.IsExcluded);
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
        // Ответ тренера за пловца, которым аккаунт стал без метки тренера («Me», привязка сайта), —
        // ответ этого аккаунта (TrainingAnswerFallback).
        var answers = TrainingAnswerFallback.WithSwimmerAnswers(
            rows.Where(r => r.UserId is int u && memberIds.Contains(u)).ToDictionary(r => r.UserId!.Value),
            rows.Where(r => r.SwimmerId != null).ToDictionary(r => r.SwimmerId!.Value),
            resolved);
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

        var laneView = await BuildLaneViewAsync(hubGroupId, schedule, date, start,
            members.ToDictionary(m => m.UserId, m => (m.Name, m.Gender)), resolved, answers, swimmerAnswers,
            viewerUserId, isManager);

        var dto = new TrainingRsvpDto
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
        // Связь с пловцом, уровень и полоса «Needs you» (CF4) — только управляющему.
        if (isManager) await TrainingCoachNeeds.FillAsync(_db, hubGroupId, LanePlanKey.Session(date, start), dto, resolved);
        return dto;
    }
}
