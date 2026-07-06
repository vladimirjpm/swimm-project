using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

public interface IAdminRepository
{
    Task<List<UserDto>> GetUsersAsync();
    Task<List<RoleDto>> GetRolesAsync();
    Task<RoleOperationResult> AddRoleAsync(int userId, int roleId);
    Task<bool> RemoveRoleAsync(int userId, int roleId);
    Task<bool> SetUserActiveAsync(int userId, bool isActive);
    Task<AdminStatsDto> GetStatsAsync();
    Task<List<ImportHistoryDto>> GetImportHistoryAsync();
    Task<bool> SetImportApprovedAsync(int id, bool approved);
    Task<List<CompetitionAdminDto>> GetCompetitionsAsync();
    Task<bool> UpdateCompetitionAsync(int id, bool isAward, bool showCombineAllResults, IReadOnlyCollection<string> categoryKeys);
    Task<List<CompetitionEventDto>> GetCompetitionEventsAsync();
    Task<UserDetailDto?> GetUserDetailsAsync(int userId);
}
