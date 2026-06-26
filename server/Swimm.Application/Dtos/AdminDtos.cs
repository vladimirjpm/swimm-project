namespace Swimm.Application.Dtos;

public class UserDto
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? SwimmerId { get; set; }
    public string[] Roles { get; set; } = [];
}

public class RoleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class AdminStatsDto
{
    public int Users { get; set; }
    public long Results { get; set; }
    public int Competitions { get; set; }
    public int Swimmers { get; set; }
    public int Clubs { get; set; }
}

public class ImportHistoryDto
{
    public int Id { get; set; }
    public int CompetitionId { get; set; }
    public string CompetitionName { get; set; } = "";
    public string CompetitionDate { get; set; } = "";
    public string ImportFileName { get; set; } = "";
    public DateTime ImportDate { get; set; }
    public bool Approved { get; set; }
}

public class CompetitionAdminDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Date { get; set; } = "";
    public string PoolType { get; set; } = "";
    public string Country { get; set; } = "";
    public bool IsMasters { get; set; }
    public bool IsAward { get; set; }
    public bool ShowCombineAllResults { get; set; }
    public int ResultCount { get; set; }
    public bool HasImportHistory { get; set; }
    /// <summary>Дата последнего импорта этого соревнования (null — импортов нет).</summary>
    public DateTime? ImportDate { get; set; }
    /// <summary>Статус подтверждения последнего импорта.</summary>
    public bool Approved { get; set; }
    // ── Многодневные соревнования ──
    public int? EventId { get; set; }
    public string? EventName { get; set; }
    public int? DayNumber { get; set; }
    public string? SubName { get; set; }
}

public class CompetitionEventDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int DayCount { get; set; }
    public int ResultCount { get; set; }
}

public class UserDetailDto
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? SwimmerId { get; set; }
    public string? LinkedSwimmerName { get; set; }
    public string[] Roles { get; set; } = [];
    public bool HasLocalPassword { get; set; }
    public bool LocalEmailConfirmed { get; set; }
    public int LocalFailedLoginCount { get; set; }
    public DateTime? LocalLockoutEnd { get; set; }
    public List<ExternalLoginDto> ExternalLogins { get; set; } = [];
    public List<LoginHistoryItemDto> LoginHistory { get; set; } = [];
}

public class ExternalLoginDto
{
    public string Provider { get; set; } = "";
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LoginHistoryItemDto
{
    public string Provider { get; set; } = "";
    public string? IpAddress { get; set; }
    public DateTime LoginAt { get; set; }
}

public enum RoleOperationResult
{
    Ok,
    UserNotFound,
    RoleNotFound,
    AlreadyAssigned,
    NotAssigned
}
