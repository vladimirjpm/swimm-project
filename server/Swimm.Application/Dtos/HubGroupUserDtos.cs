namespace Swimm.Application.Dtos;

/// <summary>Может ли текущий пользователь создать группу — для показа/скрытия кнопки в UI.</summary>
public sealed class HubGroupCreateEligibilityDto
{
    public bool CanCreate { get; set; }
    public string? Reason { get; set; }

    /// <summary>Сколько ещё групп можно создать (по HubGroupMaxPerUser); null — без лимита (админ).</summary>
    public int? Remaining { get; set; }
}

/// <summary>Со-тренер группы — для списка управления в пользовательской форме.</summary>
public sealed class HubGroupManagerDto
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
