namespace Swimm.Application.Dtos;

/// <summary>Входные данные заявки владельца группы на официальный статус (фаза 8.7).</summary>
public sealed class HubGroupClubRequestInputDto
{
    public int ClubId { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Заявка — вид владельца группы («Моя группа»): последняя заявка (любого статуса) для группы.
/// </summary>
public sealed class MyHubGroupClubRequestDto
{
    public int Id { get; set; }
    public int ClubId { get; set; }
    public string ClubName { get; set; } = "";
    public string? Message { get; set; }
    /// <summary>pending | approved | rejected</summary>
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
}

/// <summary>Заявка — вид админа (Admin/HubGroupClubRequests): для одобрения/отклонения.</summary>
public sealed class HubGroupClubRequestAdminRowDto
{
    public int Id { get; set; }
    public int HubGroupId { get; set; }
    public string HubGroupName { get; set; } = "";
    public string HubGroupSlug { get; set; } = "";
    public int RequesterUserId { get; set; }
    public string RequesterDisplayName { get; set; } = "";
    public string RequesterEmail { get; set; } = "";
    public int ClubId { get; set; }
    public string ClubName { get; set; } = "";
    public string? Message { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecidedByDisplayName { get; set; }
}
