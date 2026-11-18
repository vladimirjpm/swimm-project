using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="IContentPermissionService"/>
/// <remarks>
/// Исключения читаются из БД на каждый запрос (одна строка по PK) — без кэша: правка в
/// /Admin/Users (ProdAdmin, другой процесс) действует на проде сразу, мост кэша не нужен.
/// Политика — из <see cref="ISettingsService"/>, её прод перечитывает через мост.
/// </remarks>
public class ContentPermissionService : IContentPermissionService
{
    private readonly SwimmDbContext _db;
    private readonly ISettingsService _settings;

    public ContentPermissionService(SwimmDbContext db, ISettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    private static readonly ContentPermissionsDto Nothing = new();

    public async Task<ContentPermissionsDto> GetAsync(int userId, bool isAdmin)
    {
        var row = await _db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.CanAddPhotos, u.CanAddVideos, u.CanPublish })
            .FirstOrDefaultAsync();
        if (row == null) return Nothing;

        return ContentPermissionRules.Evaluate(_settings, isAdmin, row.CanAddPhotos, row.CanAddVideos, row.CanPublish);
    }

    public async Task<string?> DenyReasonAsync(int userId, bool isAdmin, params ContentAction[] actions)
    {
        var permissions = await GetAsync(userId, isAdmin);
        foreach (var action in actions)
        {
            if (!ContentPermissionRules.Allows(permissions, action))
                return ContentPermissionRules.DeniedMessage(action);
        }
        return null;
    }
}
