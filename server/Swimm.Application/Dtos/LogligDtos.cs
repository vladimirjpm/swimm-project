namespace Swimm.Application.Dtos;

/// <summary>Карточка игрока loglig.com (Players/Details/{id}).</summary>
public sealed record LogligPlayerCard(
    string FullName,
    int? BirthYear,
    string? Gender,
    string? ClubName,
    IReadOnlyList<LogligResultRow> Results);

/// <summary>Строка таблицы личных рекордов (pld-pb-table) карточки игрока.</summary>
public sealed record LogligResultRow(
    string EventRaw,        // «100 חופשי» как на сайте
    string? Distance,       // «100», «4X50»
    string? StyleName,      // freestyle/... по маппингу; null если стиль не распознан
    bool IsRelay,
    int PoolLength,         // 25/50
    string TimeRaw,         // «01:32.68»
    int? TimeMillisecond,
    DateTime Date,          // из dd/MM/yyyy, DateTimeKind.Utc
    string CompetitionName);
