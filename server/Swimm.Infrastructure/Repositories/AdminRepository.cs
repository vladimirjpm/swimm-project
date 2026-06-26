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
        var user = await _db.AppUsers.FindAsync(userId);
        if (user == null)
            return RoleOperationResult.UserNotFound;

        if (!await _db.AppRoles.AnyAsync(r => r.Id == roleId))
            return RoleOperationResult.RoleNotFound;

        if (await _db.AppUserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId))
            return RoleOperationResult.AlreadyAssigned;

        _db.AppUserRoles.Add(new AppUserRole { UserId = userId, RoleId = roleId });
        // Смена ролей → инвалидируем активные сессии (форс ре-логин с новыми ролями).
        BumpSecurityStamp(user);
        await _db.SaveChangesAsync();

        return RoleOperationResult.Ok;
    }

    public async Task<bool> RemoveRoleAsync(int userId, int roleId)
    {
        var link = await _db.AppUserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId);

        if (link == null) return false;

        _db.AppUserRoles.Remove(link);

        // Смена ролей → инвалидируем активные сессии (форс ре-логин с новыми ролями).
        var user = await _db.AppUsers.FindAsync(userId);
        if (user != null) BumpSecurityStamp(user);

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetUserActiveAsync(int userId, bool isActive)
    {
        var user = await _db.AppUsers.FindAsync(userId);
        if (user == null) return false;

        user.IsActive = isActive;
        // Деактивация → отзыв активных сессий. Бампаем и при реактивации (безвредно).
        BumpSecurityStamp(user);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Сменить штамп безопасности — инвалидирует все активные cookie-сессии пользователя.</summary>
    private static void BumpSecurityStamp(AppUser user)
    {
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;
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

    public async Task<UserDetailDto?> GetUserDetailsAsync(int userId)
    {
        var user = await _db.AppUsers
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.ExternalLogins)
            .Include(u => u.LocalCredential)
            .Include(u => u.Swimmer)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return null;

        var loginHistory = await _db.UserLoginHistory
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.LoginAt)
            .Take(20)
            .ToListAsync();

        return new UserDetailDto
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            SwimmerId = user.SwimmerId,
            LinkedSwimmerName = user.Swimmer != null
                ? $"{user.Swimmer.LastName} {user.Swimmer.FirstName}".Trim()
                : null,
            Roles = user.UserRoles.Select(r => r.Role.Name).ToArray(),
            HasLocalPassword = user.LocalCredential?.PasswordHash != null,
            LocalEmailConfirmed = user.LocalCredential?.EmailConfirmed ?? false,
            LocalFailedLoginCount = user.LocalCredential?.FailedLoginCount ?? 0,
            LocalLockoutEnd = user.LocalCredential?.LockoutEnd,
            ExternalLogins = user.ExternalLogins
                .OrderBy(e => e.Provider)
                .Select(e => new ExternalLoginDto
                {
                    Provider = e.Provider,
                    Email = e.Email,
                    EmailVerified = e.EmailVerified,
                    CreatedAt = e.CreatedAt
                }).ToList(),
            LoginHistory = loginHistory
                .Select(h => new LoginHistoryItemDto
                {
                    Provider = h.Provider,
                    IpAddress = h.IpAddress,
                    LoginAt = h.LoginAt
                }).ToList()
        };
    }

    public async Task<List<CompetitionAdminDto>> GetCompetitionsAsync()
    {
        // Дни одного события держим рядом: сначала по событию, потом по номеру дня;
        // обычные (без события) — по убыванию даты.
        return await _db.Competitions
            .AsNoTracking()
            .OrderBy(c => c.EventId == null ? 1 : 0)
            .ThenByDescending(c => c.Event != null ? c.Event.StartDate : null)
            .ThenBy(c => c.EventId)
            .ThenBy(c => c.DayNumber)
            .ThenByDescending(c => c.Date)
            .ThenBy(c => c.Name)
            .Select(c => new CompetitionAdminDto
            {
                Id = c.Id,
                Name = c.Name,
                Date = c.Date,
                PoolType = c.PoolType,
                Country = c.Country,
                IsMasters = c.IsMasters,
                IsAward = c.IsAward,
                ShowCombineAllResults = c.ShowCombineAllResults,
                ResultCount = _db.Results.Count(r => r.CompetitionId == c.Id),
                HasImportHistory = _db.ImportHistory.Any(h => h.CompetitionId == c.Id),
                ImportDate = _db.ImportHistory
                    .Where(h => h.CompetitionId == c.Id)
                    .OrderByDescending(h => h.ImportDate)
                    .Select(h => (DateTime?)h.ImportDate)
                    .FirstOrDefault(),
                Approved = _db.ImportHistory
                    .Where(h => h.CompetitionId == c.Id)
                    .OrderByDescending(h => h.ImportDate)
                    .Select(h => h.Approved)
                    .FirstOrDefault(),
                EventId = c.EventId,
                EventName = c.Event != null ? c.Event.Name : null,
                DayNumber = c.DayNumber,
                SubName = c.SubName
            })
            .ToListAsync();
    }

    public async Task<bool> UpdateCompetitionFlagsAsync(int id, bool isMasters, bool isAward, bool showCombineAllResults)
    {
        var comp = await _db.Competitions.FindAsync(id);
        if (comp == null) return false;

        comp.IsMasters = isMasters;
        comp.IsAward = isAward;
        comp.ShowCombineAllResults = showCombineAllResults;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<CompetitionEventDto>> GetCompetitionEventsAsync()
    {
        return await _db.CompetitionEvents
            .AsNoTracking()
            .OrderByDescending(e => e.StartDate)
            .ThenBy(e => e.Name)
            .Select(e => new CompetitionEventDto
            {
                Id = e.Id,
                Name = e.Name,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                DayCount = _db.Competitions.Count(c => c.EventId == e.Id),
                ResultCount = _db.Results.Count(r => r.Competition.EventId == e.Id)
            })
            .ToListAsync();
    }
}
