using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="IUserMediaPublicationService"/>
public class UserMediaPublicationService : IUserMediaPublicationService
{
    private readonly SwimmDbContext _db;

    public UserMediaPublicationService(SwimmDbContext db) => _db = db;

    public async Task<(bool Success, string? Error, UserMediaPublicationDto? Publication)> SubmitAsync(
        int ownerUserId, int mediaId, SubmitPublicationRequest request, bool isGroupPrivileged)
    {
        var level = request.Level?.Trim().ToLowerInvariant() ?? "";
        if (level != UserMediaPublicationLevel.Members && level != UserMediaPublicationLevel.Public)
            return (false, "level must be 'members' or 'public'", null);

        // Медиа существует и принадлежит подателю — публиковать чужое нельзя.
        var media = await _db.UserMedia.AsNoTracking()
            .Where(m => m.Id == mediaId && m.UserId == ownerUserId)
            .Select(m => new { m.Id, m.SwimmerId })
            .FirstOrDefaultAsync();
        if (media == null) return (false, "media not found", null);

        var group = await _db.HubGroups.AsNoTracking()
            .Where(g => g.Id == request.HubGroupId)
            .Select(g => new { g.Id, g.Name })
            .FirstOrDefaultAsync();
        if (group == null) return (false, "group not found", null);

        // Правило подачи 1: пловец из медиа — в ростере группы. Иначе член «Дельфин мастерс»
        // мог бы подать туда видео ребёнка, который там не плавает.
        var swimmerInRoster = await _db.HubGroupMembers.AsNoTracking()
            .AnyAsync(m => m.HubGroupId == group.Id && m.SwimmerId == media.SwimmerId);
        if (!swimmerInRoster)
            return (false, "swimmer is not in this group's roster", null);

        // Правило подачи 2: податель — активный user-член группы (админ/владелец группы
        // проходит по isGroupPrivileged — контроллер проверяет через IHubGroupPermissionService).
        if (!isGroupPrivileged)
        {
            var isActiveMember = await _db.HubGroupUserMembers.AsNoTracking()
                .AnyAsync(m => m.HubGroupId == group.Id && m.UserId == ownerUserId
                               && m.Status == HubGroupUserMemberStatus.Active);
            if (!isActiveMember)
                return (false, "you are not an active member of this group", null);
        }

        // Одна публикация на пару (медиа, группа): повторная подача возвращает строку в pending
        // (после reject/withdraw-approve), уровень можно поменять при переподаче.
        var existing = await _db.UserMediaPublications
            .FirstOrDefaultAsync(p => p.UserMediaId == mediaId && p.HubGroupId == group.Id);

        // Заявка от админа/владельца группы к самому себе — сразу approved (нет смысла в inbox).
        var status = isGroupPrivileged ? UserMediaPublicationStatus.Approved : UserMediaPublicationStatus.Pending;

        UserMediaPublication entity;
        if (existing != null)
        {
            if (existing.Status == UserMediaPublicationStatus.Pending
                || existing.Status == UserMediaPublicationStatus.Approved)
                return (false, "publication already exists", null);

            existing.Level = level;
            existing.Status = status;
            existing.DecidedByUserId = isGroupPrivileged ? ownerUserId : null;
            existing.DecidedAt = isGroupPrivileged ? DateTime.UtcNow : null;
            existing.CreatedAt = DateTime.UtcNow;
            entity = existing;
        }
        else
        {
            entity = new UserMediaPublication
            {
                UserMediaId = mediaId,
                HubGroupId = group.Id,
                Level = level,
                Status = status,
                DecidedByUserId = isGroupPrivileged ? ownerUserId : null,
                DecidedAt = isGroupPrivileged ? DateTime.UtcNow : null,
                CreatedAt = DateTime.UtcNow,
            };
            _db.UserMediaPublications.Add(entity);
        }

        await _db.SaveChangesAsync();

        return (true, null, new UserMediaPublicationDto
        {
            Id = entity.Id,
            UserMediaId = entity.UserMediaId,
            HubGroupId = entity.HubGroupId,
            HubGroupName = group.Name,
            Level = entity.Level,
            Status = entity.Status,
            CreatedAt = entity.CreatedAt,
            DecidedAt = entity.DecidedAt,
        });
    }

    public async Task<bool> WithdrawAsync(int ownerUserId, int mediaId, int hubGroupId)
    {
        // IDOR: владение проверяем через join на UserMedia.UserId.
        var entity = await _db.UserMediaPublications
            .Where(p => p.UserMediaId == mediaId && p.HubGroupId == hubGroupId
                        && p.Media!.UserId == ownerUserId)
            .FirstOrDefaultAsync();
        if (entity == null) return false;

        _db.UserMediaPublications.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<UserMediaPublicationDto>> GetForOwnerAsync(int ownerUserId)
        => await _db.UserMediaPublications.AsNoTracking()
            .Where(p => p.Media!.UserId == ownerUserId)
            .OrderByDescending(p => p.Id)
            .Select(p => new UserMediaPublicationDto
            {
                Id = p.Id,
                UserMediaId = p.UserMediaId,
                HubGroupId = p.HubGroupId,
                HubGroupName = p.HubGroup!.Name,
                Level = p.Level,
                Status = p.Status,
                CreatedAt = p.CreatedAt,
                DecidedAt = p.DecidedAt,
            })
            .ToListAsync();

    public async Task<List<PublishTargetDto>> GetPublishTargetsAsync(int ownerUserId, int mediaId)
    {
        var media = await _db.UserMedia.AsNoTracking()
            .Where(m => m.Id == mediaId && m.UserId == ownerUserId)
            .Select(m => new { m.SwimmerId })
            .FirstOrDefaultAsync();
        if (media == null) return [];

        // Владелец/админ группы проходит подачу и без user-членства (isGroupPrivileged в
        // SubmitAsync) — поэтому объединение: активный член ИЛИ владелец/админ группы.
        return await _db.HubGroups.AsNoTracking()
            .Where(g => _db.HubGroupMembers.Any(m => m.HubGroupId == g.Id && m.SwimmerId == media.SwimmerId)
                        && (g.OwnerUserId == ownerUserId
                            || _db.HubGroupAdmins.Any(a => a.HubGroupId == g.Id && a.UserId == ownerUserId)
                            || _db.HubGroupUserMembers.Any(um => um.HubGroupId == g.Id
                                && um.UserId == ownerUserId && um.Status == HubGroupUserMemberStatus.Active)))
            .OrderBy(g => g.Name)
            .Select(g => new PublishTargetDto { Id = g.Id, Name = g.Name })
            .ToListAsync();
    }

    public Task<List<GroupPublicationInboxItemDto>> GetForGroupAsync(int hubGroupId)
        => QueryGroupItems(_db.UserMediaPublications.AsNoTracking()
            .Where(p => p.HubGroupId == hubGroupId
                        && p.Status != UserMediaPublicationStatus.Rejected)
            .OrderBy(p => p.Status == UserMediaPublicationStatus.Pending ? 0 : 1)
            .ThenByDescending(p => p.Id));

    public Task<List<GroupPublicationInboxItemDto>> GetModerationFeedAsync(int userId, bool isSiteAdmin)
        => QueryGroupItems(_db.UserMediaPublications.AsNoTracking()
            .Where(p => isSiteAdmin
                        || p.HubGroup!.OwnerUserId == userId
                        || _db.HubGroupAdmins.Any(a => a.HubGroupId == p.HubGroupId && a.UserId == userId))
            .OrderBy(p => p.Status == UserMediaPublicationStatus.Pending ? 0 : 1)
            .ThenByDescending(p => p.Id));

    public Task<List<GroupPublicationInboxItemDto>> GetApprovedForGroupAsync(int hubGroupId, string level)
        => QueryGroupItems(_db.UserMediaPublications.AsNoTracking()
            .Where(p => p.HubGroupId == hubGroupId
                        && p.Status == UserMediaPublicationStatus.Approved
                        && p.Level == level)
            .OrderByDescending(p => p.Id));

    private static Task<List<GroupPublicationInboxItemDto>> QueryGroupItems(IQueryable<UserMediaPublication> query)
        => query
            .Select(p => new GroupPublicationInboxItemDto
            {
                Id = p.Id,
                HubGroupId = p.HubGroupId,
                HubGroupName = p.HubGroup!.Name,
                Level = p.Level,
                Status = p.Status,
                CreatedAt = p.CreatedAt,
                MediaType = p.Media!.MediaType,
                SourceType = p.Media.SourceType,
                Url = p.Media.Url,
                OwnerUserId = p.Media.UserId,
                OwnerEmail = p.Media.User!.Email,
                SwimmerId = p.Media.SwimmerId,
                SwimmerName = (p.Media.Swimmer!.LastName + " " + p.Media.Swimmer.FirstName).Trim(),
                ResultId = p.Media.ResultId,
                ResultLabel = p.Media.ResultRecord != null
                    ? p.Media.ResultRecord.Style.Name + " " + p.Media.ResultRecord.Distance
                      + " · " + p.Media.ResultRecord.Competition.Date
                    : null,
            })
            .ToListAsync();

    public async Task<List<VisibleResultMediaDto>> GetVisibleForResultsAsync(
        int? competitionId, int? eventId, string? groupSlug, int? userId)
    {
        if (competitionId == null && eventId == null && string.IsNullOrWhiteSpace(groupSlug)) return [];

        // Скоуп: соревнование / все дни события (многодневные) / group-режим страницы
        // результатов (?group=slug — заплывы пловцов из ростера группы, соревнования разные).
        // Для competitionId/eventId включаем и медиа уровня «соревнование» (ResultId == null,
        // CompetitionId задан) — таб Media шапки соревнования. Group-скоуп по-прежнему только
        // заплывы (ResultId != null): без этого в ленту группы утекало бы личное
        // swimmer-level медиа ростера, не привязанное ни к какому соревнованию.
        IQueryable<UserMedia> mediaInScope = _db.UserMedia.AsNoTracking();
        mediaInScope = eventId != null
            ? mediaInScope.Where(m => m.CompetitionId != null && m.Competition!.EventId == eventId)
            : competitionId != null
                ? mediaInScope.Where(m => m.CompetitionId == competitionId)
                : mediaInScope.Where(m => m.ResultId != null && _db.HubGroupMembers.Any(gm =>
                    gm.SwimmerId == m.SwimmerId && gm.HubGroup!.Slug == groupSlug));

        // 1. Своё медиа — видно владельцу целиком (private в том числе).
        var mine = userId == null
            ? []
            : await mediaInScope
                .Where(m => m.UserId == userId)
                .Select(m => new VisibleResultMediaDto
                {
                    ResultId = m.ResultId,
                    MediaType = m.MediaType,
                    SourceType = m.SourceType,
                    Url = m.Url,
                })
                .ToListAsync();

        // 2. Одобренные публикации: public — всем; members — активным членам группы публикации.
        var published = await _db.UserMediaPublications.AsNoTracking()
            .Where(p => p.Status == UserMediaPublicationStatus.Approved
                        && (eventId != null
                            ? p.Media!.CompetitionId != null && p.Media.Competition!.EventId == eventId
                            : competitionId != null
                                ? p.Media!.CompetitionId == competitionId
                                : p.Media!.ResultId != null && _db.HubGroupMembers.Any(gm =>
                                    gm.SwimmerId == p.Media.SwimmerId && gm.HubGroup!.Slug == groupSlug))
                        && (p.Level == UserMediaPublicationLevel.Public
                            || (userId != null && _db.HubGroupUserMembers.Any(um =>
                                um.HubGroupId == p.HubGroupId && um.UserId == userId
                                && um.Status == HubGroupUserMemberStatus.Active))))
            .Select(p => new VisibleResultMediaDto
            {
                ResultId = p.Media!.ResultId,
                MediaType = p.Media.MediaType,
                SourceType = p.Media.SourceType,
                Url = p.Media.Url,
            })
            .ToListAsync();

        // Дедуп: одно и то же медиа может быть и своим, и опубликованным в нескольких группах.
        return mine.Concat(published)
            .GroupBy(v => new { v.ResultId, v.Url })
            .Select(g => g.First())
            .OrderBy(v => v.ResultId)
            .ToList();
    }

    public async Task<List<VisibleResultMediaDto>> GetVisibleForSwimmerAsync(int swimmerId, int? userId)
    {
        if (swimmerId <= 0) return [];

        // Скоуп — всё медиа пловца (любой уровень привязки). Правила видимости те же, что и в
        // GetVisibleForResultsAsync: своё (любое) + approved public (всем) + approved members
        // (активным членам группы публикации). Отличие только в скоупе (по SwimmerId, не по
        // соревнованию), поэтому логику намеренно дублируем минимально, не размывая контракт.
        var mine = userId == null
            ? []
            : await _db.UserMedia.AsNoTracking()
                .Where(m => m.SwimmerId == swimmerId && m.UserId == userId)
                .Select(m => new VisibleResultMediaDto
                {
                    ResultId = m.ResultId,
                    MediaType = m.MediaType,
                    SourceType = m.SourceType,
                    Url = m.Url,
                })
                .ToListAsync();

        var published = await _db.UserMediaPublications.AsNoTracking()
            .Where(p => p.Status == UserMediaPublicationStatus.Approved
                        && p.Media!.SwimmerId == swimmerId
                        && (p.Level == UserMediaPublicationLevel.Public
                            || (userId != null && _db.HubGroupUserMembers.Any(um =>
                                um.HubGroupId == p.HubGroupId && um.UserId == userId
                                && um.Status == HubGroupUserMemberStatus.Active))))
            .Select(p => new VisibleResultMediaDto
            {
                ResultId = p.Media!.ResultId,
                MediaType = p.Media.MediaType,
                SourceType = p.Media.SourceType,
                Url = p.Media.Url,
            })
            .ToListAsync();

        return mine.Concat(published)
            .GroupBy(v => new { v.ResultId, v.Url })
            .Select(g => g.First())
            .ToList();
    }

    public async Task<bool> DecideAsync(int hubGroupId, int publicationId, bool approve, int decidedByUserId)
    {
        var entity = await _db.UserMediaPublications
            .FirstOrDefaultAsync(p => p.Id == publicationId && p.HubGroupId == hubGroupId);
        if (entity == null) return false;

        entity.Status = approve ? UserMediaPublicationStatus.Approved : UserMediaPublicationStatus.Rejected;
        entity.DecidedByUserId = decidedByUserId;
        entity.DecidedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }
}
