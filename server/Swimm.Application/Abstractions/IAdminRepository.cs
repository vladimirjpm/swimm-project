using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

public interface IAdminRepository
{
    Task<List<UserDto>> GetUsersAsync();
    Task<List<RoleDto>> GetRolesAsync();
    Task<RoleOperationResult> AddRoleAsync(int userId, int roleId);
    Task<bool> RemoveRoleAsync(int userId, int roleId);
    Task<bool> SetUserActiveAsync(int userId, bool isActive);

    /// <summary>Принудительный выход со всех устройств (бамп SecurityStamp), аккаунт остаётся активным.</summary>
    Task<bool> ForceSignOutAsync(int userId);

    /// <summary>Сводка по логинам (онлайн сейчас, логины 7/30д, фейлы 7д) для панели Admin/Users.</summary>
    Task<LoginStatsDto> GetLoginStatsAsync();

    /// <summary>Ретеншн журнала логинов: удалить события старше 90 дней. Возвращает число удалённых.</summary>
    Task<int> CleanupLoginHistoryAsync();
    Task<AdminStatsDto> GetStatsAsync();
    Task<List<ImportHistoryDto>> GetImportHistoryAsync();
    Task<bool> SetImportApprovedAsync(int id, bool approved);
    Task<List<CompetitionAdminDto>> GetCompetitionsAsync();
    Task<bool> UpdateCompetitionAsync(int id, bool isAward, bool showCombineAllResults, IReadOnlyCollection<string> categoryKeys);
    Task<List<CompetitionEventDto>> GetCompetitionEventsAsync();
    Task<UserDetailDto?> GetUserDetailsAsync(int userId);
}
