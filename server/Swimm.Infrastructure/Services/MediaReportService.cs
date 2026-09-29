using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Жалобы «Report» на медиа (Р62, docs/data-integrity.md). Правила — <see cref="MediaReportRules"/>,
/// прятание на витринах — <c>MediaPublicationAudience.NotHidden</c> и лента опубликованного
/// (<c>UserMediaPublicationService.PublishedItemsAsync</c>): обе смотрят на
/// <see cref="UserMedia.ModerationState"/>, который ставит этот сервис.
/// </summary>
public class MediaReportService : IMediaReportService
{
    private readonly SwimmDbContext _db;
    private readonly ISettingsService _settings;
    private readonly IAdminAuditService _audit;

    public MediaReportService(SwimmDbContext db, ISettingsService settings, IAdminAuditService audit)
    {
        _db = db;
        _settings = settings;
        _audit = audit;
    }

    public async Task<MediaReportSubmitResult> ReportAsync(
        int reporterUserId, int mediaId, SubmitMediaReportRequest request, bool isSiteAdmin)
    {
        var reason = request.Reason?.Trim().ToLowerInvariant();
        if (!MediaReportRules.IsKnownReason(reason))
            return new(MediaReportOutcome.Invalid, "Choose a reason");
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (comment is { Length: > MediaReportRules.MaxCommentLength })
            return new(MediaReportOutcome.Invalid, $"Keep it under {MediaReportRules.MaxCommentLength} characters");
        if (reason == MediaReportRules.ReasonOther && comment == null)
            return new(MediaReportOutcome.Invalid, "Tell us what's wrong");

        var media = await _db.UserMedia.FirstOrDefaultAsync(m => m.Id == mediaId);
        if (media == null) return new(MediaReportOutcome.NotFound, "Media not found");
        if (media.UserId == reporterUserId)
            return new(MediaReportOutcome.OwnMedia, "This is your own media — delete it or withdraw it from sharing instead");

        // Жаловаться можно на то, что видно (то же правило, что у показа и лайка): иначе перебором id
        // можно было бы прятать чужое закрытое. Спрятанное уже никому не видно — 404.
        var visible = await _db.UserMediaPublications.AsNoTracking()
            .Where(p => p.UserMediaId == mediaId && p.Status == UserMediaPublicationStatus.Approved)
            .Where(MediaPublicationAudience.CanSee(_db, reporterUserId, isSiteAdmin))
            .AnyAsync();
        if (!visible) return new(MediaReportOutcome.NotFound, "Media not found");

        if (await _db.MediaReports.AnyAsync(r => r.UserMediaId == mediaId && r.ReporterUserId == reporterUserId))
            return new(MediaReportOutcome.Accepted, AlreadyReported: true);

        _db.MediaReports.Add(new MediaReport
        {
            UserMediaId = mediaId,
            ReporterUserId = reporterUserId,
            Reason = reason!,
            Comment = comment,
            Status = MediaReportRules.StatusOpen,
        });
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Двойной клик: вторая вставка упёрлась в уникальный индекс (медиа, аккаунт).
            _db.ChangeTracker.Clear();
            return new(MediaReportOutcome.Accepted, AlreadyReported: true);
        }

        // Порог — по ОТКРЫТЫМ жалобам: закрытые решением «оставить» заново не прячут.
        var open = await _db.MediaReports.CountAsync(r => r.UserMediaId == mediaId && r.Status == MediaReportRules.StatusOpen);
        if (media.ModerationState == null && open >= MediaReportRules.HideThreshold(_settings))
        {
            media.ModerationState = MediaReportRules.StateUnderReview;
            await _db.SaveChangesAsync();
        }
        return new(MediaReportOutcome.Accepted);
    }

    public async Task<List<MediaReportQueueItemDto>> GetQueueAsync(bool open, int take = 200)
    {
        // Очередь — по медиа: одна карточка на видео со всеми его жалобами.
        var mediaIds = await _db.MediaReports.AsNoTracking()
            .Where(r => open ? r.Status == MediaReportRules.StatusOpen : r.Status != MediaReportRules.StatusOpen)
            .GroupBy(r => r.UserMediaId)
            .Select(g => new { MediaId = g.Key, Last = g.Max(r => r.CreatedAt) })
            .OrderByDescending(x => x.Last)
            .Take(take)
            .ToListAsync();
        if (mediaIds.Count == 0) return [];
        var ids = mediaIds.Select(x => x.MediaId).ToList();

        var items = await _db.UserMedia.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new MediaReportQueueItemDto
            {
                MediaId = m.Id,
                MediaType = m.MediaType,
                SourceType = m.SourceType,
                Url = m.Url,
                OwnerUserId = m.UserId,
                OwnerEmail = m.User.Email,
                SwimmerId = m.SwimmerId,
                SwimmerName = (m.Swimmer.LastName + " " + m.Swimmer.FirstName).Trim(),
                ResultLabel = m.ResultRecord != null
                    ? m.ResultRecord.Style.Name + " " + m.ResultRecord.Distance + " · " + m.ResultRecord.Competition.Date
                    : null,
                ModerationState = m.ModerationState,
            })
            .ToListAsync();

        var publications = await _db.UserMediaPublications.AsNoTracking()
            .Where(p => ids.Contains(p.UserMediaId))
            .Select(p => new
            {
                p.UserMediaId,
                Row = new MediaReportPublicationRowDto
                {
                    TargetType = p.TargetType,
                    TargetName = p.HubGroup != null ? p.HubGroup.Name : (p.Club != null ? p.Club.Name : ""),
                    TargetSlug = p.HubGroup != null ? p.HubGroup.Slug : null,
                    Level = p.Level,
                    Status = p.Status,
                    // HubGroupTrustRules; клубная публикация — доверенная (её решает админ сайта).
                    Trusted = p.HubGroup == null || p.HubGroup.IsTrusted || p.HubGroup.IsOfficial,
                },
            })
            .ToListAsync();

        var reports = await _db.MediaReports.AsNoTracking()
            .Where(r => ids.Contains(r.UserMediaId))
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.UserMediaId,
                Row = new MediaReportRowDto
                {
                    Id = r.Id,
                    ReporterEmail = r.Reporter.Email,
                    Reason = r.Reason,
                    Comment = r.Comment,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt,
                },
            })
            .ToListAsync();

        var last = mediaIds.ToDictionary(x => x.MediaId, x => x.Last);
        foreach (var item in items)
        {
            item.Publications = publications.Where(p => p.UserMediaId == item.MediaId).Select(p => p.Row).ToList();
            item.Reports = reports.Where(r => r.UserMediaId == item.MediaId).Select(r => r.Row).ToList();
            item.LastReportAt = last[item.MediaId];
        }
        return items.OrderByDescending(i => i.LastReportAt).ToList();
    }

    public Task<int> CountOpenAsync() =>
        _db.MediaReports.AsNoTracking()
            .Where(r => r.Status == MediaReportRules.StatusOpen)
            .Select(r => r.UserMediaId)
            .Distinct()
            .CountAsync();

    public async Task<bool> DecideAsync(int mediaId, bool keep, int adminUserId)
    {
        var media = await _db.UserMedia.FirstOrDefaultAsync(m => m.Id == mediaId);
        if (media == null) return false;

        var now = DateTime.UtcNow;
        var openReports = await _db.MediaReports
            .Where(r => r.UserMediaId == mediaId && r.Status == MediaReportRules.StatusOpen)
            .ToListAsync();
        foreach (var r in openReports)
        {
            r.Status = keep ? MediaReportRules.StatusKept : MediaReportRules.StatusRemoved;
            r.DecidedByUserId = adminUserId;
            r.DecidedAt = now;
        }

        var rejected = 0;
        if (keep)
        {
            // Оставить = медиа снова видно там, где было опубликовано. Снятое раньше публикации
            // не возвращает: они отклонены, владелец подаст заново.
            media.ModerationState = null;
        }
        else
        {
            media.ModerationState = MediaReportRules.StateRemoved;
            var publications = await _db.UserMediaPublications
                .Where(p => p.UserMediaId == mediaId && p.Status != UserMediaPublicationStatus.Rejected)
                .ToListAsync();
            foreach (var p in publications)
            {
                p.Status = UserMediaPublicationStatus.Rejected;
                p.DecidedByUserId = adminUserId;
                p.DecidedAt = now;
            }
            rejected = publications.Count;
        }
        await _db.SaveChangesAsync();

        await _audit.LogAsync(keep ? "media.report.keep" : "media.report.remove", "UserMedia", mediaId.ToString(),
            keep
                ? $"Медиа #{mediaId} оставлено: закрыто жалоб {openReports.Count}, медиа снова видно"
                : $"Медиа #{mediaId} снято по жалобам: закрыто жалоб {openReports.Count}, отклонено публикаций {rejected}",
            new { mediaId, reports = openReports.Select(r => r.Id).ToList(), rejectedPublications = rejected });
        return true;
    }

    public async Task<MediaReportDeleteTrail?> CaptureBeforeOwnerDeleteAsync(int ownerUserId, int mediaId)
    {
        var media = await _db.UserMedia.AsNoTracking()
            .Where(m => m.Id == mediaId && m.UserId == ownerUserId)
            .Select(m => new { m.Url, m.SwimmerId, m.ModerationState })
            .FirstOrDefaultAsync();
        if (media == null) return null;

        var reports = await _db.MediaReports.AsNoTracking()
            .Where(r => r.UserMediaId == mediaId)
            .Select(r => new { r.Reason, r.Status, r.ReporterUserId })
            .ToListAsync();
        if (reports.Count == 0) return null;

        var open = reports.Count(r => r.Status == MediaReportRules.StatusOpen);
        return new MediaReportDeleteTrail(
            mediaId, media.Url, media.SwimmerId, media.ModerationState,
            Open: open, Decided: reports.Count - open,
            Reasons: reports.GroupBy(r => r.Reason).ToDictionary(g => g.Key, g => g.Count()),
            ReporterUserIds: reports.Select(r => r.ReporterUserId).Distinct().ToList());
    }

    public Task LogOwnerDeleteAsync(MediaReportDeleteTrail t) =>
        // Актор — владелец (из HTTP-контекста). Решение по жалобам он этим не принимал, но без
        // записи медиа молча выпало бы из очереди /Admin/MediaReports.
        _audit.LogAsync("media.report.owner-delete", "UserMedia", t.MediaId.ToString(),
            $"Владелец удалил медиа #{t.MediaId} с жалобами: открытых {t.Open}, разобранных {t.Decided}"
                + (t.ModerationState != null ? $", состояние {t.ModerationState}" : ""),
            t);
}
