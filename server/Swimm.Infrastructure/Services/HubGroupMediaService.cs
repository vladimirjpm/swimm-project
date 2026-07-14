using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="IHubGroupMediaService"/>
public class HubGroupMediaService : IHubGroupMediaService
{
    private static readonly HashSet<string> AllowedMediaTypes = ["image", "video", "album"];
    private static readonly HashSet<string> AllowedSourceTypes = ["youtube", "vimeo", "album", "other"];

    private readonly SwimmDbContext _db;

    public HubGroupMediaService(SwimmDbContext db) => _db = db;

    public async Task<List<HubGroupMediaDto>> GetGalleryAsync(int hubGroupId)
        => await _db.HubGroupMedia.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && m.TrainingId == null
                        && m.Visibility == HubGroupMediaVisibility.Public)
            .OrderBy(m => m.Id)
            .Select(ToDto)
            .ToListAsync();

    public async Task<List<HubGroupMemberMediaDto>> GetMembersMediaAsync(int hubGroupId)
        => await _db.HubGroupMedia.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && m.TrainingId == null
                        && m.Visibility == HubGroupMediaVisibility.Members)
            .OrderByDescending(m => m.Id)
            .Select(m => new HubGroupMemberMediaDto
            {
                Id = m.Id,
                MediaType = m.MediaType,
                SourceType = m.SourceType,
                Url = m.Url,
                Caption = m.Caption,
                CreatedAt = m.CreatedAt,
                SwimmerId = m.SwimmerId,
                SwimmerName = m.Swimmer != null
                    ? (m.Swimmer.LastName + " " + m.Swimmer.FirstName).Trim()
                    : null,
                SwimmerNameEn = m.Swimmer != null
                    ? (m.Swimmer.LastNameEn + " " + m.Swimmer.FirstNameEn).Trim()
                    : null,
                ResultId = m.ResultId,
                ResultLabel = m.Result != null
                    ? m.Result.Style.Name + " " + m.Result.Distance + " · "
                      + m.Result.Competition.Date + " · " + m.Result.Competition.Name
                    : null
            })
            .ToListAsync();

    public async Task<(bool Success, string? Error, int Id)> AddAsync(
        int hubGroupId, HubGroupMediaInputDto input, int createdByUserId)
    {
        var mediaType = input.MediaType?.Trim().ToLowerInvariant() ?? "";
        var sourceType = input.SourceType?.Trim().ToLowerInvariant() ?? "";
        var url = input.Url?.Trim() ?? "";
        var caption = string.IsNullOrWhiteSpace(input.Caption) ? null : input.Caption.Trim();

        if (!AllowedMediaTypes.Contains(mediaType))
            return (false, "media_type must be one of: image, video, album", 0);

        if (!AllowedSourceTypes.Contains(sourceType))
            return (false, "source_type must be one of: youtube, vimeo, album, other", 0);

        // Инвариант: MediaType=album ⇔ SourceType=album.
        if ((mediaType == "album") != (sourceType == "album"))
            return (false, "media_type 'album' requires source_type 'album' and vice versa", 0);

        if (string.IsNullOrWhiteSpace(url) || url.Length > 1000)
            return (false, "url is required (max 1000 chars)", 0);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return (false, "url must be an absolute https URL", 0);

        if (caption is { Length: > 200 })
            return (false, "caption must be at most 200 chars", 0);

        if (input.TrainingId is { } trainingId)
        {
            var belongs = await _db.TrainingSessions.AsNoTracking()
                .AnyAsync(s => s.Id == trainingId && s.HubGroupId == hubGroupId);
            if (!belongs) return (false, "training_id does not belong to this group", 0);
        }

        /* 2B′ — members-слой (тренерские разборы). Для медиа тренировок visibility/якоря
           не применяются: у тренировки своя аудитория, разборы — отдельный слой. */
        var visibility = HubGroupMediaVisibility.Public;
        int? swimmerId = null;
        long? resultId = null;

        if (input.TrainingId is null)
        {
            visibility = string.IsNullOrWhiteSpace(input.Visibility)
                ? HubGroupMediaVisibility.Public
                : input.Visibility.Trim().ToLowerInvariant();

            if (visibility != HubGroupMediaVisibility.Public && visibility != HubGroupMediaVisibility.Members)
                return (false, "visibility must be 'public' or 'members'", 0);

            if (visibility == HubGroupMediaVisibility.Public)
            {
                // Публично вешать медиа на персону нельзя (см. docs/favorites-media-phase2-design.md).
                if (input.SwimmerId != null || input.ResultId != null)
                    return (false, "swimmer_id/result_id anchors require visibility 'members'", 0);
            }
            else
            {
                // Members-слой — только официальная группа (решение Влада: тренерские разборы
                // доступны группам с подтверждённой связью с клубом).
                var isOfficial = await _db.HubGroups.AsNoTracking()
                    .Where(g => g.Id == hubGroupId)
                    .Select(g => g.IsOfficial)
                    .FirstOrDefaultAsync();
                if (!isOfficial)
                    return (false, "members media requires an official group", 0);

                if (input.ResultId is { } rid)
                {
                    // Якорь-заплыв: SwimmerId денормализуем из заплыва (даже если прислали свой —
                    // источник правды заплыв). Эстафеты не привязываем: строка Result у эстафеты
                    // закреплена за одним «первым» пловцом, разбор вышел бы адресован не тому.
                    var result = await _db.Results.AsNoTracking()
                        .Where(r => r.Id == rid)
                        .Select(r => new { r.SwimmerId, IsRelay = r.RelayId != null })
                        .FirstOrDefaultAsync();
                    if (result is null) return (false, "result_id not found", 0);
                    if (result.IsRelay) return (false, "relay results cannot be anchored", 0);
                    resultId = rid;
                    swimmerId = result.SwimmerId;
                }
                else if (input.SwimmerId is { } sid)
                {
                    var exists = await _db.Swimmers.AsNoTracking().AnyAsync(s => s.Id == sid);
                    if (!exists) return (false, "swimmer_id not found", 0);
                    swimmerId = sid;
                }
                // Оба null — общее members-медиа группы, тоже валидно.
            }
        }

        var entity = new HubGroupMedia
        {
            HubGroupId = hubGroupId,
            TrainingId = input.TrainingId,
            Visibility = visibility,
            SwimmerId = swimmerId,
            ResultId = resultId,
            MediaType = mediaType,
            SourceType = sourceType,
            Url = url,
            Caption = caption,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow,
        };

        _db.HubGroupMedia.Add(entity);
        await _db.SaveChangesAsync();
        return (true, null, entity.Id);
    }

    public async Task<bool> DeleteAsync(int hubGroupId, int mediaId)
    {
        var entity = await _db.HubGroupMedia
            .FirstOrDefaultAsync(m => m.Id == mediaId && m.HubGroupId == hubGroupId);
        if (entity == null) return false;

        _db.HubGroupMedia.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    internal static readonly System.Linq.Expressions.Expression<Func<HubGroupMedia, HubGroupMediaDto>> ToDto = m =>
        new HubGroupMediaDto
        {
            Id = m.Id,
            MediaType = m.MediaType,
            SourceType = m.SourceType,
            Url = m.Url,
            Caption = m.Caption,
        };
}
