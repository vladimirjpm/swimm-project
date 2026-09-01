using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// DTO одного правила начисления клубных очков.
/// Структура намеренно зеркалит клиентский интерфейс PointsRule (в прошлом — статика
/// club-points-config.json; она удалена, правила приходят только этим эндпоинтом).
/// </summary>
public sealed class ClubPointsRuleDto
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>Дата вступления в силу в формате "yyyy-MM-dd".</summary>
    [JsonPropertyName("effectiveFrom")]
    public string EffectiveFrom { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>"all", "masters" или "non-masters".</summary>
    [JsonPropertyName("scope")]
    public string Scope { get; init; } = string.Empty;

    [JsonPropertyName("defaultPoints")]
    public int DefaultPoints { get; init; }

    [JsonPropertyName("maxScoringPlace")]
    public int? MaxScoringPlace { get; init; }

    /// <summary>Очки по местам: ключ — номер места (строкой), значение — количество очков.</summary>
    [JsonPropertyName("pointsByPlace")]
    public Dictionary<string, int> PointsByPlace { get; init; } = [];
}
