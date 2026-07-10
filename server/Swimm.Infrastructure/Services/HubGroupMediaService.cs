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
            .Where(m => m.HubGroupId == hubGroupId && m.TrainingId == null)
            .OrderBy(m => m.Id)
            .Select(ToDto)
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

        var entity = new HubGroupMedia
        {
            HubGroupId = hubGroupId,
            TrainingId = input.TrainingId,
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
