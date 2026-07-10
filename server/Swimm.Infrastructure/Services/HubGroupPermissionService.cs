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
        var owner = await _db.HubGroups.AsNoTracking()
            .Where(g => g.Id == hubGroupId)
            .Select(g => (int?)g.OwnerUserId)
            .FirstOrDefaultAsync();
        if (owner == null) return HubGroupPermissions.NotFound(isAdmin);

        var isOwner = owner.Value == userId;
        var isCoManager = !isOwner && await _db.HubGroupManagers
            .AnyAsync(m => m.HubGroupId == hubGroupId && m.UserId == userId);

        return new HubGroupPermissions(
            Exists: true,
            IsAdmin: isAdmin,
            IsOwner: isOwner,
            IsCoManager: isCoManager,
            CanEdit: isAdmin || isOwner || isCoManager,
            CanManageCoTrainers: isAdmin || isOwner,
            CanDelete: isAdmin || isOwner,
            CanChangeOwner: isAdmin);
    }
}
