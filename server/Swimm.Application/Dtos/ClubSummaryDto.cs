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
    /// <summary>
    /// Id клуба — адрес его страницы (<c>/clubs/{id}</c>): по имени она не открывается.
    /// Зачёт ключуется ИМЕНЕМ (ClubStandingKey.ByName), поэтому у строки-тёзки здесь id
    /// первого встреченного клуба; дубли клубов вычищены (см. club-merge), тёзок нет.
    /// </summary>
    [JsonPropertyName("clubId")] public int ClubId { get; init; }
    [JsonPropertyName("points")] public int Points { get; init; }
    /// <summary>Уникальные пловцы клуба в выборке — по <c>SwimmerId</c>, не по фамилии.</summary>
    [JsonPropertyName("swimmerCount")] public int SwimmerCount { get; init; }
    /// <summary>
    /// Заплывы, ПРИНЁСШИЕ клубу очки (место попало в шкалу правила), а не все заплывы клуба.
    /// В UI подписывается «scoring swims»: при включённом Combine All Results число падает
    /// примерно вдвое — топ-N становится один на дисциплину, а не на возрастную полосу.
    /// </summary>
    [JsonPropertyName("successfulCount")] public int SuccessfulCount { get; init; }
    [JsonPropertyName("gold")] public int Gold { get; init; }
    [JsonPropertyName("silver")] public int Silver { get; init; }
    [JsonPropertyName("bronze")] public int Bronze { get; init; }
}
