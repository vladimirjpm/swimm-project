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
        int ownerUserId, int mediaId, SubmitPublicationRequest request, bool isPrivileged)
    {
        var level = request.Level?.Trim().ToLowerInvariant() ?? "";
        if (level != UserMediaPublicationLevel.Members && level != UserMediaPublicationLevel.Public)
            return (false, "level must be 'members' or 'public'", null);

        var targetType = request.TargetType?.Trim().ToLowerInvariant() ?? "";
        if (targetType != UserMediaPublicationTarget.Group && targetType != UserMediaPublicationTarget.Club)
            return (false, "target must be 'group' or 'club'", null);

        // Медиа существует и принадлежит подателю — публиковать чужое нельзя.
        var media = await _db.UserMedia.AsNoTracking()
            .Where(m => m.Id == mediaId && m.UserId == ownerUserId)
            .Select(m => new { m.Id, m.SwimmerId })
            .FirstOrDefaultAsync();
        if (media == null) return (false, "media not found", null);

        var isClub = targetType == UserMediaPublicationTarget.Club;

        // У КЛУБА нет аккаунтов-участников, поэтому у уровня members там нет аудитории —
        // молча принять такую заявку значило бы спрятать медиа ни для кого.
        if (isClub && level == UserMediaPublicationLevel.Members)
            return (false, "club publications can only be public", null);

        string targetName;
        if (isClub)
        {
            // Ростер клуба бесплатный: пловец числится за клубом в справочнике федерации.
            // Отдельного «состава» вести не нужно — это и есть главная выгода клубной цели.
            var club = await _db.Clubs.AsNoTracking()
                .Where(c => c.Id == request.TargetId && c.MergedIntoId == null)
                .Select(c => new { c.Id, c.Name })
                .FirstOrDefaultAsync();
            if (club == null) return (false, "club not found", null);

            var swimmerInClub = await _db.Swimmers.AsNoTracking()
                .AnyAsync(sw => sw.Id == media.SwimmerId && sw.ClubId == club.Id);
            if (!swimmerInClub)
                return (false, "swimmer does not belong to this club", null);

            targetName = club.Name;
        }
        else
        {
            var group = await _db.HubGroups.AsNoTracking()
                .Where(g => g.Id == request.TargetId)
                .Select(g => new { g.Id, g.Name })
                .FirstOrDefaultAsync();
            if (group == null) return (false, "group not found", null);

            // Правило подачи 1: пловец из медиа — в ростере группы. Иначе член «Дельфин мастерс»
            // мог бы подать туда видео ребёнка, который там не плавает.
            // Скрытый владельцем клубный пловец в ростер не входит (IsExcluded).
            var swimmerInRoster = await _db.HubGroupMembers.AsNoTracking()
                .AnyAsync(m => m.HubGroupId == group.Id && m.SwimmerId == media.SwimmerId && !m.IsExcluded);
            if (!swimmerInRoster)
                return (false, "swimmer is not in this group's roster", null);

            // Правило подачи 2: податель — активный user-член группы (админ/владелец группы
            // проходит по isPrivileged — контроллер проверяет через IHubGroupPermissionService).
            // У клуба этого правила НЕТ и быть не может: членства в клубе как аккаунта не
            // существует, поэтому там подача открыта владельцу медиа, а фильтром служит
            // модерация.
            if (!isPrivileged)
            {
                var isActiveMember = await _db.HubGroupUserMembers.AsNoTracking()
                    .AnyAsync(m => m.HubGroupId == group.Id && m.UserId == ownerUserId
                                   && m.Status == HubGroupUserMemberStatus.Active);
                if (!isActiveMember)
                    return (false, "you are not an active member of this group", null);
            }

            targetName = group.Name;
        }

        // Одна публикация на пару (медиа, цель): повторная подача возвращает строку в pending
        // (после reject/withdraw-approve), уровень можно поменять при переподаче.
        var existing = await _db.UserMediaPublications
            .FirstOrDefaultAsync(p => p.UserMediaId == mediaId
                                      && (isClub ? p.ClubId == request.TargetId
                                                 : p.HubGroupId == request.TargetId));

        // Заявка от того, кто и так решает по этой цели, — сразу approved (нет смысла в inbox).
        var status = isPrivileged ? UserMediaPublicationStatus.Approved : UserMediaPublicationStatus.Pending;

        UserMediaPublication entity;
        if (existing != null)
        {
            if (existing.Status == UserMediaPublicationStatus.Pending
                || existing.Status == UserMediaPublicationStatus.Approved)
                return (false, "publication already exists", null);

            existing.Level = level;
            existing.Status = status;
            existing.DecidedByUserId = isPrivileged ? ownerUserId : null;
            existing.DecidedAt = isPrivileged ? DateTime.UtcNow : null;
            existing.CreatedAt = DateTime.UtcNow;
            entity = existing;
        }
        else
        {
            entity = new UserMediaPublication
            {
                UserMediaId = mediaId,
                TargetType = targetType,
                HubGroupId = isClub ? null : request.TargetId,
                ClubId = isClub ? request.TargetId : null,
                Level = level,
                Status = status,
                DecidedByUserId = isPrivileged ? ownerUserId : null,
                DecidedAt = isPrivileged ? DateTime.UtcNow : null,
                CreatedAt = DateTime.UtcNow,
            };
            _db.UserMediaPublications.Add(entity);
        }

        await _db.SaveChangesAsync();

        return (true, null, new UserMediaPublicationDto
        {
            Id = entity.Id,
            UserMediaId = entity.UserMediaId,
            TargetType = entity.TargetType,
            TargetId = request.TargetId,
            TargetName = targetName,
            Level = entity.Level,
            Status = entity.Status,
            CreatedAt = entity.CreatedAt,
            DecidedAt = entity.DecidedAt,
        });
    }

    public async Task<bool> WithdrawAsync(int ownerUserId, int mediaId, string targetType, int targetId)
    {
        var isClub = targetType == UserMediaPublicationTarget.Club;

        // IDOR: владение проверяем через join на UserMedia.UserId.
        var entity = await _db.UserMediaPublications
            .Where(p => p.UserMediaId == mediaId && p.Media!.UserId == ownerUserId
                        && (isClub ? p.ClubId == targetId : p.HubGroupId == targetId))
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
                TargetType = p.TargetType,
                TargetId = p.HubGroupId ?? p.ClubId ?? 0,
                TargetName = p.HubGroup != null ? p.HubGroup.Name : (p.Club != null ? p.Club.Name : ""),
                Level = p.Level,
                Status = p.Status,
                CreatedAt = p.CreatedAt,
                DecidedAt = p.DecidedAt,
            })
            .ToListAsync();

    public async Task<List<PublishTargetDto>> GetPublishTargetsAsync(
        int ownerUserId, int mediaId, bool isSiteAdmin)
    {
        var media = await _db.UserMedia.AsNoTracking()
            .Where(m => m.Id == mediaId && m.UserId == ownerUserId)
            .Select(m => new { m.SwimmerId })
            .FirstOrDefaultAsync();
        if (media == null) return [];

        // Группы: пловец в ростере И податель свой. Владелец/админ группы проходит подачу и
        // без user-членства (isPrivileged в SubmitAsync) — поэтому объединение.
        // ⚠ isSiteAdmin входит сюда наравне: раньше цели его не видели, хотя SubmitAsync
        // пускал — админ сайта не получал ни одной цели, хотя подача от него прошла бы и сразу
        // стала approved (диагноз в docs/media-page.md §9).
        var groups = await _db.HubGroups.AsNoTracking()
            .Where(g => _db.HubGroupMembers.Any(m => m.HubGroupId == g.Id && m.SwimmerId == media.SwimmerId && !m.IsExcluded)
                        && (isSiteAdmin
                            || g.OwnerUserId == ownerUserId
                            || _db.HubGroupAdmins.Any(a => a.HubGroupId == g.Id && a.UserId == ownerUserId)
                            || _db.HubGroupUserMembers.Any(um => um.HubGroupId == g.Id
                                && um.UserId == ownerUserId && um.Status == HubGroupUserMemberStatus.Active)))
            .OrderBy(g => g.Name)
            .Select(g => new PublishTargetDto
            {
                Type = UserMediaPublicationTarget.Group,
                Id = g.Id,
                Name = g.Name,
            })
            .ToListAsync();

        // Клуб пловца — цель без всякой ручной работы: ростер приходит из справочника
        // федерации (Swimmer.ClubId). Членства в клубе не существует, поэтому право подать
        // есть у владельца медиа, а фильтром служит модерация: заявка ложится pending, и
        // решает её админ сайта (управляющих у клуба пока нет, план §3.10).
        var club = await _db.Swimmers.AsNoTracking()
            .Where(sw => sw.Id == media.SwimmerId && sw.Club != null && sw.Club.MergedIntoId == null
                         && !sw.Club.IsPseudo)
            .Select(sw => new PublishTargetDto
            {
                Type = UserMediaPublicationTarget.Club,
                Id = sw.Club!.Id,
                Name = sw.Club.Name,
            })
            .FirstOrDefaultAsync();

        if (club != null) groups.Add(club);
        return groups;
    }

    public Task<List<GroupPublicationInboxItemDto>> GetForGroupAsync(int hubGroupId)
        => QueryGroupItems(_db.UserMediaPublications.AsNoTracking()
            .Where(p => p.HubGroupId == hubGroupId
                        && p.Status != UserMediaPublicationStatus.Rejected)
            .OrderBy(p => p.Status == UserMediaPublicationStatus.Pending ? 0 : 1)
            .ThenByDescending(p => p.Id));

    public Task<List<GroupPublicationInboxItemDto>> GetModerationFeedAsync(int userId, bool isSiteAdmin)
        => QueryGroupItems(_db.UserMediaPublications.AsNoTracking()
            // Клубные заявки модерирует только админ сайта — у клуба управляющих нет.
            .Where(p => isSiteAdmin
                        || (p.HubGroup != null
                            && (p.HubGroup.OwnerUserId == userId
                                || _db.HubGroupAdmins.Any(a => a.HubGroupId == p.HubGroupId && a.UserId == userId))))
            .OrderBy(p => p.Status == UserMediaPublicationStatus.Pending ? 0 : 1)
            .ThenByDescending(p => p.Id));

    public Task<List<GroupPublicationInboxItemDto>> GetApprovedForGroupAsync(int hubGroupId, string level)
        => QueryGroupItems(_db.UserMediaPublications.AsNoTracking()
            .Where(p => p.HubGroupId == hubGroupId
                        && p.Status == UserMediaPublicationStatus.Approved
                        && p.Level == level)
            .OrderByDescending(p => p.Id));

    public Task<List<GroupPublicationInboxItemDto>> GetApprovedForClubAsync(int clubId)
        => QueryGroupItems(_db.UserMediaPublications.AsNoTracking()
            .Where(p => p.ClubId == clubId
                        && p.Status == UserMediaPublicationStatus.Approved
                        // У клуба уровень бывает только public — но фильтр оставлен явным:
                        // он и есть граница «что видно любому посетителю».
                        && p.Level == UserMediaPublicationLevel.Public)
            .OrderByDescending(p => p.Id));

    private static Task<List<GroupPublicationInboxItemDto>> QueryGroupItems(IQueryable<UserMediaPublication> query)
        => query
            .Select(p => new GroupPublicationInboxItemDto
            {
                Id = p.Id,
                TargetType = p.TargetType,
                TargetId = p.HubGroupId ?? p.ClubId ?? 0,
                TargetName = p.HubGroup != null ? p.HubGroup.Name : (p.Club != null ? p.Club.Name : ""),
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
                // День заплыва, а если медиа подано на всё соревнование — оно само.
                CompetitionId = p.Media.ResultRecord != null
                    ? p.Media.ResultRecord.CompetitionId
                    : p.Media.CompetitionId,
            })
            .ToListAsync();

    public async Task<List<VisibleResultMediaDto>> GetVisibleForResultsAsync(
        int? competitionId, int? eventId, string? groupSlug, int? userId, bool isSiteAdmin)
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
                    gm.SwimmerId == m.SwimmerId && gm.HubGroup!.Slug == groupSlug && !gm.IsExcluded));

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

        // 2. Одобренные публикации, которые зрителю положено видеть (MediaPublicationAudience):
        // public — всем; members — участникам и управляющим группы публикации.
        var published = await _db.UserMediaPublications.AsNoTracking()
            .Where(p => p.Status == UserMediaPublicationStatus.Approved
                        && (eventId != null
                            ? p.Media!.CompetitionId != null && p.Media.Competition!.EventId == eventId
                            : competitionId != null
                                ? p.Media!.CompetitionId == competitionId
                                : p.Media!.ResultId != null && _db.HubGroupMembers.Any(gm =>
                                    gm.SwimmerId == p.Media.SwimmerId && gm.HubGroup!.Slug == groupSlug && !gm.IsExcluded)))
            .Where(MediaPublicationAudience.CanSee(_db, userId, isSiteAdmin))
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

    public async Task<List<VisibleResultMediaDto>> GetVisibleForSwimmerAsync(int swimmerId, int? userId, bool isSiteAdmin)
    {
        if (swimmerId <= 0) return [];

        // Скоуп — всё медиа пловца (любой уровень привязки). Правила видимости те же, что и в
        // GetVisibleForResultsAsync: своё (любое) + одобренные публикации по общему правилу
        // аудитории (MediaPublicationAudience). Отличие только в скоупе (по SwimmerId, не по
        // соревнованию).
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
            .Where(p => p.Status == UserMediaPublicationStatus.Approved && p.Media!.SwimmerId == swimmerId)
            .Where(MediaPublicationAudience.CanSee(_db, userId, isSiteAdmin))
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

    public async Task<bool> DecideAsync(
        string targetType, int targetId, int publicationId, bool approve, int decidedByUserId)
    {
        var isClub = targetType == UserMediaPublicationTarget.Club;

        // Цель в запросе обязательна и сверяется со строкой: иначе решение по чужой заявке
        // прошло бы через ручку своей группы.
        var entity = await _db.UserMediaPublications
            .FirstOrDefaultAsync(p => p.Id == publicationId
                                      && (isClub ? p.ClubId == targetId : p.HubGroupId == targetId));
        if (entity == null) return false;

        entity.Status = approve ? UserMediaPublicationStatus.Approved : UserMediaPublicationStatus.Rejected;
        entity.DecidedByUserId = decidedByUserId;
        entity.DecidedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }
}
