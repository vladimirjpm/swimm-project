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

public enum RoleOperationResult
{
    Ok,
    UserNotFound,
    RoleNotFound,
    AlreadyAssigned,
    NotAssigned
}
