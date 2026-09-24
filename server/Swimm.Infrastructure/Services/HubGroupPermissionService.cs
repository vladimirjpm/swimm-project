using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="IHubGroupPermissionService"/>
public class HubGroupPermissionService : IHubGroupPermissionService
{
    private readonly SwimmDbContext _db;

    public HubGroupPermissionService(SwimmDbContext db)
    {
        _db = db;
    }

    public async Task<HubGroupPermissions> GetPermissionsAsync(int hubGroupId, int userId, bool isAdmin)
    {
        var group = await _db.HubGroups.AsNoTracking()
            .Where(g => g.Id == hubGroupId)
            .Select(g => new { g.OwnerUserId, g.IsTest })
            .FirstOrDefaultAsync();
        if (group == null) return HubGroupPermissions.NotFound(isAdmin);

        // Тестовая группа для не-тестового зрителя не существует: все ручки, проверяющие
        // Exists, отвечают 404 сами (test-personas-plan.md).
        if (group.IsTest && !await TestGroupAccess.CanSeeAsync(_db, userId, isAdmin))
            return HubGroupPermissions.NotFound(isAdmin);

        var isOwner = group.OwnerUserId == userId;
        var isGroupAdmin = !isOwner && await _db.HubGroupAdmins
            .AnyAsync(m => m.HubGroupId == hubGroupId && m.UserId == userId);

        return new HubGroupPermissions(
            Exists: true,
            IsAdmin: isAdmin,
            IsOwner: isOwner,
            IsGroupAdmin: isGroupAdmin,
            CanEdit: isAdmin || isOwner || isGroupAdmin,
            CanManageAdmins: isAdmin || isOwner,
            CanDelete: isAdmin || isOwner,
            CanChangeOwner: isAdmin);
    }
}
