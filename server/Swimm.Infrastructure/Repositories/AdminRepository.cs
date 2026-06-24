using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

public class AdminRepository : IAdminRepository
{
    private readonly SwimmDbContext _db;

    public AdminRepository(SwimmDbContext db)
    {
        _db = db;
    }

    public async Task<List<UserDto>> GetUsersAsync()
    {
        return await _db.AppUsers
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Email = u.Email,
                DisplayName = u.DisplayName,
                AvatarUrl = u.AvatarUrl,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                SwimmerId = u.SwimmerId,
                Roles = u.UserRoles.Select(r => r.Role.Name).ToArray()
            })
            .ToListAsync();
    }

    public async Task<List<RoleDto>> GetRolesAsync()
    {
        return await _db.AppRoles
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .Select(r => new RoleDto { Id = r.Id, Name = r.Name })
            .ToListAsync();
    }

    public async Task<RoleOperationResult> AddRoleAsync(int userId, int roleId)
    {
        if (!await _db.AppUsers.AnyAsync(u => u.Id == userId))
            return RoleOperationResult.UserNotFound;

        if (!await _db.AppRoles.AnyAsync(r => r.Id == roleId))
            return RoleOperationResult.RoleNotFound;

        if (await _db.AppUserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId))
            return RoleOperationResult.AlreadyAssigned;

        _db.AppUserRoles.Add(new AppUserRole { UserId = userId, RoleId = roleId });
        await _db.SaveChangesAsync();

        return RoleOperationResult.Ok;
    }

    public async Task<bool> RemoveRoleAsync(int userId, int roleId)
    {
        var link = await _db.AppUserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId);

        if (link == null) return false;

        _db.AppUserRoles.Remove(link);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetUserActiveAsync(int userId, bool isActive)
    {
        var user = await _db.AppUsers.FindAsync(userId);
        if (user == null) return false;

        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<AdminStatsDto> GetStatsAsync()
    {
        return new AdminStatsDto
        {
            Users = await _db.AppUsers.CountAsync(),
            Results = await _db.Results.CountAsync(),
            Competitions = await _db.Competitions.CountAsync(),
            Swimmers = await _db.Swimmers.CountAsync(),
            Clubs = await _db.Clubs.CountAsync()
        };
    }

    public async Task<List<ImportHistoryDto>> GetImportHistoryAsync()
    {
        return await _db.ImportHistory
            .AsNoTracking()
            .Include(h => h.Competition)
            .OrderByDescending(h => h.ImportDate)
            .Select(h => new ImportHistoryDto
            {
                Id = h.Id,
                CompetitionId = h.CompetitionId,
                CompetitionName = h.Competition!.Name,
                CompetitionDate = h.Competition.Date,
                ImportFileName = h.ImportFileName,
                ImportDate = h.ImportDate,
                Approved = h.Approved
            })
            .ToListAsync();
    }

    public async Task<bool> SetImportApprovedAsync(int id, bool approved)
    {
        var entry = await _db.ImportHistory.FindAsync(id);
        if (entry == null) return false;

        entry.Approved = approved;
        await _db.SaveChangesAsync();
        return true;
    }
}
