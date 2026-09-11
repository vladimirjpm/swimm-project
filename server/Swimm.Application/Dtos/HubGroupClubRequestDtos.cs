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

    /// <summary>
    /// Что случится с другими группами при одобрении (только у pending): уйдут из каталога и
    /// будут переименованы. Админ видит это ДО кнопки — план П4.
    /// </summary>
    public HubGroupClubApproveImpactDto? ApproveImpact { get; set; }
}

/// <summary>
/// Последствия одобрения официальной группы клуба для ОСТАЛЬНЫХ групп (П4 плана подписки):
/// подписанные на клуб уйдут из каталога (по ссылке работают), совпавшие с клубом имена получат
/// « · community». Писем владельцам не шлём — они видят плашку в «My groups».
/// </summary>
public sealed class HubGroupClubApproveImpactDto
{
    /// <summary>Имена групп, подписанных на клуб, — после одобрения их не будет в каталоге.</summary>
    public List<string> LeaveCatalog { get; set; } = [];

    /// <summary>«Старое имя → новое» у групп, чьё имя совпало с клубом или официальной группой.</summary>
    public List<string> Renamed { get; set; } = [];

    public bool IsEmpty => LeaveCatalog.Count == 0 && Renamed.Count == 0;
}
