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
/// Тренировки группы как записи и ссылки к ним (<see cref="ITrainingSessionService"/>, Е1–Е2
/// плана docs/plans/group-events-media-plan.md) и лента поверх них (показатели, «You», «Sets &amp;
/// times» — Т1а docs/plans/trainings-tab-redesign-plan.md). Сам времён из таблиц НЕ читает — только
/// «есть ли они»; повторы для ленты берёт у <see cref="IHubGroupTrainingRepository"/> (И16: показ
/// времён — только там), счёт — <see cref="TrainingFeed"/>. Без кэша: данные группы приватные,
/// правят кликом.
/// </summary>
public class TrainingSessionService : ITrainingSessionService
{
    private readonly SwimmDbContext _db;
    private readonly IHubGroupMediaService _media;
    private readonly IHubGroupTrainingRepository _trainings;

    public TrainingSessionService(SwimmDbContext db, IHubGroupMediaService media, IHubGroupTrainingRepository trainings)
    {
        _db = db;
        _media = media;
        _trainings = trainings;
    }

    private static DateOnly Today(DateOnly? todayLocal) =>
        todayLocal ?? DateOnly.FromDateTime(IsraelTime.ToLocal(DateTime.UtcNow));

    /// <summary>
    /// Кто смотрит — для флагов «моё / могу удалить» у ссылок; <paramref name="NewSince"/> задан —
    /// медиа других позже него помечаются «новым» (Т1б).
    /// </summary>
    private sealed record Viewer(int UserId, bool IsManager, DateTime? NewSince = null);

    public async Task<TrainingSessionListDto> ListAsync(int hubGroupId, int viewerUserId, bool isManager)
    {
        var sessions = await _db.TrainingSessions.AsNoTracking()
            .Where(s => s.HubGroupId == hubGroupId)
            .OrderByDescending(s => s.Date).ThenByDescending(s => s.StartTime).ThenByDescending(s => s.Id)
            .Select(s => new
            {
                Session = s,
                // Только «есть ли» — сами времена здесь не нужны (И16).
                HasResults = s.Results.Any(),
            })
            .ToListAsync();

        var seen = await HubGroupSeenStore.GetAsync(_db, hubGroupId, viewerUserId);
        var viewer = new Viewer(viewerUserId, isManager, HubGroupSeenStore.NewMediaSince(seen, DateTime.UtcNow));
        var media = await LoadMediaAsync(hubGroupId, sessions.Select(s => s.Session.Id).ToList(), viewer);
        var feed = await LoadFeedAsync(hubGroupId,
            sessions.Where(s => s.HasResults).Select(s => s.Session).ToList(), viewerUserId);
        return new TrainingSessionListDto
        {
            Sessions = sessions.Select(s => WithFeed(ToDto(s.Session, s.HasResults, media), feed)).ToList(),
            CanManage = isManager,
            CanAddMedia = isManager || await MembersAddMediaAsync(hubGroupId),
            NewMediaCount = media.Values.Sum(list => list.Count(m => m.IsNew)),
        };
    }

    public Task MarkSeenAsync(int hubGroupId, int viewerUserId) =>
        HubGroupSeenStore.TouchAsync(_db, hubGroupId, viewerUserId, s => s.TrainingsSeenAt = DateTime.UtcNow);

    public async Task<TrainingSessionSaveResult> CreateAsync(int hubGroupId, int actorUserId,
        TrainingSessionInputDto input, DateOnly? todayLocal = null)
    {
        var group = await _db.HubGroups.AsNoTracking()
            .Where(g => g.Id == hubGroupId)
            .Select(g => new { g.TrainingSchedule })
            .FirstOrDefaultAsync();
        if (group == null) return TrainingSessionSaveResult.Fail(404, "Group not found.");

        var (value, error) = TrainingSessionRules.Normalize(input, Today(todayLocal));
        if (error != null) return TrainingSessionSaveResult.Fail(400, error);
        var v = value!;

        var dateTime = ToDbDate(v.Date);
        var sameDay = await _db.TrainingSessions.CountAsync(s => s.HubGroupId == hubGroupId && s.Date == dateTime);
        if (sameDay >= TrainingSessionRules.MaxPerDay)
            return TrainingSessionSaveResult.Fail(400,
                $"This day already has {TrainingSessionRules.MaxPerDay} trainings — edit one of them instead.");

        // Не задано — из расписания: начало, если в этот день ровно одно занятие (тогда тренировка
        // связана с ним — ключ RSVP «дата + начало»), и бассейн группы.
        var schedule = GroupTrainingSchedule.Parse(group.TrainingSchedule);
        var start = v.StartTime ?? SingleSlotStart(schedule, v.Date);
        var schedulePool = schedule.PoolType?.Trim();
        var pool = v.PoolType
                   ?? (schedulePool is { Length: > 0 and <= TrainingSessionRules.PoolTypeMax } ? schedulePool : "");

        var session = new TrainingSession
        {
            HubGroupId = hubGroupId,
            ExternalTrainingId = TrainingSessionRules.NewExternalId(),
            Date = dateTime,
            StartTime = start,
            Title = v.Title,
            Note = v.Note,
            PoolType = pool,
            Type = v.Type ?? TrainingSessionType.Swim,
            CreatedByUserId = actorUserId,
            CreatedAt = DateTime.UtcNow,
        };
        _db.TrainingSessions.Add(session);
        await _db.SaveChangesAsync();

        return TrainingSessionSaveResult.Ok(ToDto(session, hasResults: false,
            new Dictionary<int, List<TrainingSessionMediaDto>>()));
    }

    public async Task<TrainingSessionSaveResult> UpdateAsync(int hubGroupId, int sessionId, int actorUserId,
        TrainingSessionInputDto input, DateOnly? todayLocal = null)
    {
        var session = await _db.TrainingSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.HubGroupId == hubGroupId);
        if (session == null) return TrainingSessionSaveResult.Fail(404, "Training not found.");

        var (value, error) = TrainingSessionRules.Normalize(input, Today(todayLocal));
        if (error != null) return TrainingSessionSaveResult.Fail(400, error);
        var v = value!;

        var hasResults = await HasResultsAsync(sessionId);
        var dateTime = ToDbDate(v.Date);
        var pool = v.PoolType ?? session.PoolType;     // не прислали — оставить как было
        if (hasResults && (dateTime != session.Date || pool != session.PoolType))
            return TrainingSessionSaveResult.Fail(400,
                "This training has recorded times — its date and pool can’t change.");

        if (dateTime != session.Date)
        {
            var sameDay = await _db.TrainingSessions.CountAsync(s => s.HubGroupId == hubGroupId && s.Date == dateTime);
            if (sameDay >= TrainingSessionRules.MaxPerDay)
                return TrainingSessionSaveResult.Fail(400,
                    $"That day already has {TrainingSessionRules.MaxPerDay} trainings.");
        }

        session.Date = dateTime;
        session.StartTime = v.StartTime;
        session.Title = v.Title;
        session.Note = v.Note;
        session.PoolType = pool;
        if (v.Type != null) session.Type = v.Type;   // не прислали — оставить как было
        await _db.SaveChangesAsync();

        var media = await LoadMediaAsync(hubGroupId, [session.Id], new Viewer(actorUserId, IsManager: true));
        var feed = hasResults ? await LoadFeedAsync(hubGroupId, [session], actorUserId) : NoFeed;
        return TrainingSessionSaveResult.Ok(WithFeed(ToDto(session, hasResults, media), feed));
    }

    public async Task<TrainingSessionSetsDto?> GetSetsAsync(int hubGroupId, int sessionId, int viewerUserId,
        bool isManager)
    {
        var session = await _db.TrainingSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.HubGroupId == hubGroupId)
            .Select(s => new { s.Id, s.KeySetNo })
            .FirstOrDefaultAsync();
        if (session == null) return null;

        var repeats = await _trainings.GetRepeatsAsync(hubGroupId, [sessionId]);
        var mine = (await MySwimmersQuery.LoadAsync(_db, hubGroupId, viewerUserId))
            .Select(m => m.SwimmerId).ToHashSet();
        var swimmerIds = repeats.Select(r => r.SwimmerId).Distinct().ToList();
        var levels = (await _db.HubGroupSwimmerLevels.AsNoTracking()
                .Where(l => l.HubGroupId == hubGroupId && swimmerIds.Contains(l.SwimmerId))
                .Select(l => new { l.SwimmerId, l.Level!.Id, l.Level.Rank, l.Level.Name, l.Level.Color })
                .ToListAsync())
            .ToDictionary(l => l.SwimmerId, l => new TrainingFeed.Level(l.Id, l.Rank, l.Name, l.Color));

        return new TrainingSessionSetsDto
        {
            SessionId = session.Id,
            KeySetNo = session.KeySetNo,
            CanManage = isManager,
            Sets = TrainingFeed.Sets(repeats, session.KeySetNo, mine, levels),
        };
    }

    public async Task<TrainingSessionSaveResult> SetKeySetAsync(int hubGroupId, int sessionId, int actorUserId,
        TrainingKeySetInputDto input)
    {
        var session = await _db.TrainingSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.HubGroupId == hubGroupId);
        if (session == null) return TrainingSessionSaveResult.Fail(404, "Training not found.");

        var repeats = await _trainings.GetRepeatsAsync(hubGroupId, [sessionId]);
        if (input.SetNo is int setNo && !repeats.Any(r => r.SetNo == setNo))
            return TrainingSessionSaveResult.Fail(400, "This training has no such set.");

        session.KeySetNo = input.SetNo;
        await _db.SaveChangesAsync();

        var media = await LoadMediaAsync(hubGroupId, [session.Id], new Viewer(actorUserId, IsManager: true));
        var feed = await FeedAsync(hubGroupId, [session], repeats, actorUserId);
        return TrainingSessionSaveResult.Ok(WithFeed(ToDto(session, repeats.Count > 0, media), feed));
    }

    public async Task<(int Status, string? Error)> DeleteAsync(int hubGroupId, int sessionId)
    {
        var session = await _db.TrainingSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.HubGroupId == hubGroupId);
        if (session == null) return (404, "Training not found.");

        if (await HasResultsAsync(sessionId)) return (400, "This training has recorded times — it can’t be deleted here.");

        // Медиа уходит с тренировкой (FK cascade); явно — чтобы не зависеть от провайдера.
        _db.HubGroupMedia.RemoveRange(await _db.HubGroupMedia
            .Where(m => m.HubGroupId == hubGroupId && m.TrainingId == sessionId).ToListAsync());
        _db.TrainingSessions.Remove(session);
        await _db.SaveChangesAsync();
        return (0, null);
    }

    public async Task<TrainingSessionMediaSaveResult> AddMediaAsync(int hubGroupId, int sessionId, int actorUserId,
        bool isManager, TrainingSessionMediaInputDto input)
    {
        var exists = await _db.TrainingSessions.AnyAsync(s => s.Id == sessionId && s.HubGroupId == hubGroupId);
        if (!exists) return TrainingSessionMediaSaveResult.Fail(404, "Training not found.");

        if (!isManager && !await MembersAddMediaAsync(hubGroupId))
            return TrainingSessionMediaSaveResult.Fail(403, "In this group only the coach adds videos to trainings.");

        var onSession = await _db.HubGroupMedia.CountAsync(m => m.HubGroupId == hubGroupId && m.TrainingId == sessionId);
        if (onSession >= TrainingSessionRules.MaxMediaPerSession)
            return TrainingSessionMediaSaveResult.Fail(400,
                $"This training already has {TrainingSessionRules.MaxMediaPerSession} links.");
        if (!isManager)
        {
            var mine = await _db.HubGroupMedia.CountAsync(m =>
                m.HubGroupId == hubGroupId && m.TrainingId == sessionId && m.CreatedByUserId == actorUserId);
            if (mine >= TrainingSessionRules.MaxMediaPerMember)
                return TrainingSessionMediaSaveResult.Fail(400,
                    $"You’ve added {TrainingSessionRules.MaxMediaPerMember} links to this training — remove one first.");
        }

        // Проверки ссылки (тип, источник, https, длины) — те же, что у медиа группы: одно место.
        var (ok, error, id) = await _media.AddAsync(hubGroupId, new HubGroupMediaInputDto
        {
            MediaType = input.MediaType,
            SourceType = input.SourceType,
            Url = input.Url,
            Caption = input.Caption,
            TrainingId = sessionId,
        }, actorUserId);
        if (!ok) return TrainingSessionMediaSaveResult.Fail(400, error ?? "Could not add the link.");

        var added = (await LoadMediaAsync(hubGroupId, [sessionId], new Viewer(actorUserId, isManager)))[sessionId]
            .Single(m => m.Id == id);
        return TrainingSessionMediaSaveResult.Ok(added);
    }

    public async Task<(int Status, string? Error)> DeleteMediaAsync(int hubGroupId, int sessionId, int mediaId,
        int actorUserId, bool isManager)
    {
        var media = await _db.HubGroupMedia.FirstOrDefaultAsync(m =>
            m.Id == mediaId && m.HubGroupId == hubGroupId && m.TrainingId == sessionId);
        if (media == null) return (404, "Link not found.");
        if (!CanDelete(media.CreatedByUserId, new Viewer(actorUserId, isManager)))
            return (403, "Only the person who added it or the coach can remove this link.");

        _db.HubGroupMedia.Remove(media);
        await _db.SaveChangesAsync();
        return (0, null);
    }

    /// <summary>Участники добавляют ссылки, если группа не поставила «только тренер» (по умолчанию — да).</summary>
    private async Task<bool> MembersAddMediaAsync(int hubGroupId)
    {
        var json = await _db.HubGroups.AsNoTracking()
            .Where(g => g.Id == hubGroupId)
            .Select(g => g.TrainingSchedule)
            .FirstOrDefaultAsync();
        return GroupTrainingSchedule.Parse(json).EffectiveTrainingMedia == GroupTrainingMedia.Members;
    }

    /// <summary>Показатели и «You» сессии — только у сессий с временами.</summary>
    private sealed record Feed(TrainingSessionStatsDto? Stats, TrainingSessionMyKeyDto? MyKey);

    private static readonly IReadOnlyDictionary<int, Feed> NoFeed = new Dictionary<int, Feed>();

    private async Task<IReadOnlyDictionary<int, Feed>> LoadFeedAsync(int hubGroupId,
        IReadOnlyList<TrainingSession> sessions, int viewerUserId)
    {
        if (sessions.Count == 0) return NoFeed;
        var repeats = await _trainings.GetRepeatsAsync(hubGroupId, sessions.Select(s => s.Id).ToList());
        return await FeedAsync(hubGroupId, sessions, repeats, viewerUserId);
    }

    private async Task<IReadOnlyDictionary<int, Feed>> FeedAsync(int hubGroupId,
        IReadOnlyList<TrainingSession> sessions, IReadOnlyList<TrainingFeed.Repeat> repeats, int viewerUserId)
    {
        // «Мои» нужны только там, где тренер поставил ★, — иначе «You» нет и запросы лишние.
        var mine = sessions.Any(s => s.KeySetNo != null)
            ? await MySwimmersQuery.LoadAsync(_db, hubGroupId, viewerUserId)
            : [];
        var bySession = repeats.ToLookup(r => r.SessionId);
        return sessions.ToDictionary(s => s.Id, s =>
        {
            var own = bySession[s.Id].ToList();
            return new Feed(TrainingFeed.Stats(own), TrainingFeed.MyKey(own, s.KeySetNo, mine));
        });
    }

    private static TrainingSessionDto WithFeed(TrainingSessionDto dto, IReadOnlyDictionary<int, Feed> feed)
    {
        if (feed.TryGetValue(dto.Id, out var f))
        {
            dto.Stats = f.Stats;
            dto.MyKey = f.MyKey;
        }
        return dto;
    }

    private Task<bool> HasResultsAsync(int sessionId) =>
        _db.TrainingSessions.Where(s => s.Id == sessionId).AnyAsync(s => s.Results.Any());

    private static bool CanDelete(int? authorUserId, Viewer viewer) =>
        viewer.IsManager || (authorUserId != null && authorUserId == viewer.UserId);

    /// <summary>
    /// Дата тренировки в базе — полночь UTC, как пишет сидер (<c>timestamp with time zone</c>:
    /// Npgsql не пишет <c>Kind = Unspecified</c> — поймано на стенде 30.09.2026, InMemory не ловит).
    /// </summary>
    private static DateTime ToDbDate(DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    /// <summary>Начало единственного занятия расписания в этот день недели; иначе null.</summary>
    private static string? SingleSlotStart(GroupTrainingSchedule schedule, DateOnly date)
    {
        var isoDay = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
        var slots = schedule.Slots.Where(s => s.IsValid && s.Day == isoDay).ToList();
        return slots.Count == 1 ? slots[0].StartTime!.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : null;
    }

    private async Task<Dictionary<int, List<TrainingSessionMediaDto>>> LoadMediaAsync(int hubGroupId,
        List<int> sessionIds, Viewer viewer)
    {
        var rows = await _db.HubGroupMedia.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && m.TrainingId != null && sessionIds.Contains(m.TrainingId.Value))
            .OrderBy(m => m.Id)
            .Select(m => new
            {
                TrainingId = m.TrainingId!.Value,
                m.Id, m.MediaType, m.SourceType, m.Url, m.Caption, m.CreatedByUserId, m.CreatedAt,
            })
            .ToListAsync();
        return rows
            .GroupBy(r => r.TrainingId)
            .ToDictionary(g => g.Key, g => g.Select(r => new TrainingSessionMediaDto
            {
                Id = r.Id,
                MediaType = r.MediaType,
                SourceType = r.SourceType,
                Url = r.Url,
                Caption = r.Caption,
                Mine = r.CreatedByUserId == viewer.UserId,
                CanDelete = CanDelete(r.CreatedByUserId, viewer),
                IsNew = viewer.NewSince is DateTime since && r.CreatedByUserId != viewer.UserId && r.CreatedAt > since,
            }).ToList());
    }

    private static TrainingSessionDto ToDto(TrainingSession s, bool hasResults,
        IReadOnlyDictionary<int, List<TrainingSessionMediaDto>> media) => new()
    {
        Id = s.Id,
        Date = DateOnly.FromDateTime(s.Date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Start = s.StartTime,
        Title = s.Title,
        Plan = string.IsNullOrWhiteSpace(s.Name) ? null : s.Name,
        Note = s.Note,
        PoolType = string.IsNullOrWhiteSpace(s.PoolType) ? null : s.PoolType,
        Type = s.Type,
        HasResults = hasResults,
        KeySetNo = s.KeySetNo,
        CreatedInApp = s.ExternalTrainingId.StartsWith(TrainingSessionRules.ExternalIdPrefix, StringComparison.Ordinal),
        Media = media.TryGetValue(s.Id, out var list) ? list : [],
    };
}
