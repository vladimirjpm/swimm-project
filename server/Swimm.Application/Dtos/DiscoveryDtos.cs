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
    string? MatchedCompetitionName,
    /// <summary>Языки успешно загруженных PDF: null | "he" | "en" | "he,en".</summary>
    string? Languages);

/// <summary>Итог «синхронизации языков»: дозаполнение EN/HE-имён пловцов из двуязычной пары PDF.</summary>
public sealed class SwimmerNameSyncResult
{
    /// <summary>Уникальных пловцов (не эстафет) в протоколе.</summary>
    public int SwimmersInProtocol { get; set; }
    /// <summary>Существующим пловцам дозаполнены пустые LastNameEn/FirstNameEn.</summary>
    public int EnNamesFilled { get; set; }
    /// <summary>Пловцы, созданные из EN-протокола, канонизированы: HE-имя в основные поля, EN — в *En.</summary>
    public int Canonized { get; set; }
    /// <summary>Не найдены в БД ни по HE-, ни по EN-имени.</summary>
    public int NotFound { get; set; }
    /// <summary>Уже были синхронизированы (обе пары имён на месте).</summary>
    public int AlreadyComplete { get; set; }
}
