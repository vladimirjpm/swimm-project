using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Экран «Источники стартового протокола» соревнования в админке: что привязано и что
/// можно привязать. camelCase — как у остальных админских ответов.
/// </summary>
public sealed record CompetitionSourcesViewDto(
    [property: JsonPropertyName("competitionId")] int CompetitionId,
    [property: JsonPropertyName("competitionName")] string CompetitionName,
    /// <summary>Дни соревнования — на них ложатся привязки (у многодневки источник у каждого свой).</summary>
    [property: JsonPropertyName("days")] IReadOnlyList<CompetitionSourceDayDto> Days,
    [property: JsonPropertyName("linked")] IReadOnlyList<CompetitionSourceLinkDto> Linked,
    /// <summary>Строки «Входящих», подходящие по датам и ещё не привязанные сюда.</summary>
    [property: JsonPropertyName("candidates")] IReadOnlyList<CompetitionSourceCandidateDto> Candidates);

public sealed record CompetitionSourceDayDto(
    [property: JsonPropertyName("competitionId")] int CompetitionId,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("dayNumber")] int? DayNumber,
    [property: JsonPropertyName("subName")] string? SubName);

public sealed record CompetitionSourceLinkDto(
    [property: JsonPropertyName("orgCompId")] int OrgCompId,
    [property: JsonPropertyName("competitionId")] int CompetitionId,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("sourceName")] string? SourceName,
    /// <summary>Сколько заявок затянуто по этому compID. 0 — привязка есть, забора не было.</summary>
    [property: JsonPropertyName("entries")] int Entries);

public sealed record CompetitionSourceCandidateDto(
    [property: JsonPropertyName("discoveredId")] int DiscoveredId,
    [property: JsonPropertyName("orgCompId")] int OrgCompId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("status")] string Status,
    /// <summary>Привязан к ДРУГОМУ соревнованию — привязывать сюда почти наверняка ошибка.</summary>
    [property: JsonPropertyName("linkedElsewhere")] bool LinkedElsewhere);
