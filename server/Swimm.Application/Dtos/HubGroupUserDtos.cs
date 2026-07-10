namespace Swimm.Application.Dtos;

/// <summary>Может ли текущий пользователь создать группу — для показа/скрытия кнопки в UI.</summary>
public sealed class HubGroupCreateEligibilityDto
{
    public bool CanCreate { get; set; }
    public string? Reason { get; set; }

    /// <summary>Сколько ещё групп можно создать (по HubGroupMaxPerUser); null — без лимита (админ).</summary>
    public int? Remaining { get; set; }
}

/// <summary>Админ группы — для списка управления в пользовательской форме.</summary>
public sealed class HubGroupAdminMemberDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Участник-аккаунт группы (Sys_HubGroupUserMembers) — для приватного списка состава в
/// панели управления. Отличается от пловца (<see cref="HubGroupMemberRowDto"/>): это аккаунт.
/// </summary>
public sealed class HubGroupUserMemberRowDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    /// <summary>active | pending.</summary>
    public string Status { get; set; } = "";
    /// <summary>true = вступил сам (AddedByUserId == null), false = добавлен владельцем/админом.</summary>
    public bool SelfJoined { get; set; }
    public DateTime JoinedAt { get; set; }
}

/// <summary>Группа, в которую текущий пользователь вступил как участник-аккаунт (список «участвую»).</summary>
public sealed class JoinedHubGroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? IconUrl { get; set; }
    /// <summary>active | pending.</summary>
    public string Status { get; set; } = "";
    public DateTime JoinedAt { get; set; }
}
