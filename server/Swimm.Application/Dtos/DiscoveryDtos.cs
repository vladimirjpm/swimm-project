namespace Swimm.Application.Dtos;

/// <summary>Строка списка competitions.asp (isr.org.il).</summary>
public sealed record DiscoveredListItem(
    int OrgCompId,
    string Name,
    DateTime DateStart,
    DateTime DateEnd);

/// <summary>Детальная страница comp.asp?compID= (isr.org.il).</summary>
public sealed record DiscoveredDetails(
    string Name,
    string? Venue,
    int? LogligId,
    int DayCount);

/// <summary>Итог синхронизации «входящих» со списком сайта.</summary>
public sealed class DiscoverySyncResult
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int TotalOnSite { get; set; }
}

/// <summary>Строка «входящих» для админки.</summary>
public sealed record DiscoveredCompetitionDto(
    int Id,
    int OrgCompId,
    string Name,
    DateTime DateStart,
    DateTime DateEnd,
    string? Venue,
    int? LogligId,
    string Status,
    DateTime DiscoveredAt,
    DateTime LastSeenAt,
    string? LastError,
    /// <summary>Имя совпавшего уже-импортированного соревнования (матч по дате+имени), если есть.</summary>
    string? MatchedCompetitionName);
