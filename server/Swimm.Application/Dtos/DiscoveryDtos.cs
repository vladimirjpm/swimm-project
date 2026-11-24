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
    /// <summary>Будущих стартов, у которых в этот раз увидели перенос даты (В4, <c>DiscoverySyncRules</c>).</summary>
    public int Postponed { get; set; }
    /// <summary>Будущих стартов сезона, впервые пропавших из списка сайта (кандидаты в отменённые).</summary>
    public int Missing { get; set; }
    /// <summary>
    /// Сами изменения прогона (новые, перенесённые, обновлённые, пропавшие) — попап прогона на
    /// /Admin/Jobs (<c>JobRunDetails.Discovery</c>). Счётчики выше считаются по тем же событиям.
    /// </summary>
    public List<DiscoveryChange> Changes { get; } = [];
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
    /// <summary>Id совпавшего соревнования (для перехода в /Admin/Competitions), если есть.</summary>
    int? MatchedCompetitionId,
    /// <summary>Языки успешно загруженных PDF: null | "he" | "en" | "he,en".</summary>
    string? Languages);

/// <summary>Две ступени «нет loglig-iframe»: федерация иногда выкладывает протокол позже (решение Влада, 24.11.2026).</summary>
public static class NoLogligIframeRules
{
    /// <summary>Сколько дней после последнего дня старта «пока нет» становится «нет и не будет».</summary>
    public const int FinalAfterDays = 7;

    /// <summary>Последний день — больший из DateStart/DateEnd (у многодневки — конец); ровно 7 дней — уже «не будет».</summary>
    public static bool IsFinal(DateTime dateStart, DateTime dateEnd, DateTime now)
    {
        var lastDay = (dateEnd > dateStart ? dateEnd : dateStart).Date;
        return (now.Date - lastDay).TotalDays >= FinalAfterDays;
    }
}

/// <summary>Тексты LastError «входящих», по которым админка отличает исходы (единственное место).</summary>
public static class DiscoveryErrors
{
    /// <summary>Детали comp.asp прочитаны, loglig-iframe на странице нет.</summary>
    public const string NoLogligIframe = "Результаты на странице не опубликованы (нет loglig-iframe).";
}

/// <summary>Строка отчёта батч-бэкфилла OrgCompId (CLI, см. Program.cs --backfill-discovery-orgcompid).</summary>
public sealed record DiscoveryBackfillRow(
    int OrgCompId,
    string DiscoveredName,
    int CompetitionId,
    string CompetitionName,
    string Action);

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
