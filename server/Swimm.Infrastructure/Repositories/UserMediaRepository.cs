using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// 2A: личное owner-only медиа (Sys_UserMedia). Уровень всегда "swimmer", visibility
/// всегда "private" — см. docs/tasks/user-media-2a-sonnet.md. Формат URL валидируется
/// в контроллере через MediaUrlValidator до вызова AddAsync.
/// </summary>
public class UserMediaRepository : IUserMediaRepository
{
    private readonly SwimmDbContext _db;

    public UserMediaRepository(SwimmDbContext db)
    {
        _db = db;
    }

    public async Task<List<UserMediaDto>> GetForUserAsync(int userId, int? swimmerId = null)
    {
        var query = _db.UserMedia
            .AsNoTracking()
            .Where(m => m.UserId == userId);

        if (swimmerId != null)
            query = query.Where(m => m.SwimmerId == swimmerId.Value);

        return await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Select(m => new UserMediaDto
            {
                Id = m.Id,
                SwimmerId = m.SwimmerId,
                Level = m.Level,
                MediaType = m.MediaType,
                SourceType = m.SourceType,
                Url = m.Url,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<UserMediaDto?> AddAsync(int userId, AddUserMediaRequest request)
    {
        var swimmerExists = await _db.Swimmers.AnyAsync(s => s.Id == request.SwimmerId);
        if (!swimmerExists) return null;

        var media = new UserMedia
        {
            UserId = userId,
            SwimmerId = request.SwimmerId,
            Level = "swimmer",
            MediaType = request.MediaType,
            SourceType = request.SourceType,
            Url = request.Url,
            Visibility = "private",
            CreatedAt = DateTime.UtcNow
        };

        _db.UserMedia.Add(media);
        await _db.SaveChangesAsync();

        return new UserMediaDto
        {
            Id = media.Id,
            SwimmerId = media.SwimmerId,
            Level = media.Level,
            MediaType = media.MediaType,
            SourceType = media.SourceType,
            Url = media.Url,
            CreatedAt = media.CreatedAt
        };
    }

    public async Task<bool> RemoveAsync(int userId, int mediaId)
    {
        // IDOR: фильтруем по userId — нельзя удалить чужое медиа.
        var media = await _db.UserMedia
            .FirstOrDefaultAsync(m => m.Id == mediaId && m.UserId == userId);

        if (media == null) return false;

        _db.UserMedia.Remove(media);
        await _db.SaveChangesAsync();
        return true;
    }
}
