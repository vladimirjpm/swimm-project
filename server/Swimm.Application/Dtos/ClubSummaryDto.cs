using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Сводка по клубу в рамках источника (соревнование/событие): очки, пловцы, медали.
/// Серверный аналог клиентского <c>HelperClub.getClubsSummary</c> — нужен в paged-режиме,
/// где у клиента нет полного датасета для агрегации (фаза 3.4).
/// camelCase-ключи — под клиентский интерфейс ClubSummary (useClubSummary).
/// </summary>
public sealed class ClubSummaryDto
{
    [JsonPropertyName("club")] public string Club { get; init; } = "";
    [JsonPropertyName("points")] public int Points { get; init; }
    [JsonPropertyName("swimmerCount")] public int SwimmerCount { get; init; }
    [JsonPropertyName("successfulCount")] public int SuccessfulCount { get; init; }
    [JsonPropertyName("gold")] public int Gold { get; init; }
    [JsonPropertyName("silver")] public int Silver { get; init; }
    [JsonPropertyName("bronze")] public int Bronze { get; init; }
}
