using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// DTO категории соревнований (сводный список).
/// </summary>
public sealed class CategoryDto
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("display_order")]
    public int DisplayOrder { get; init; }
}

/// <summary>
/// DTO категории с детализацией: список соревнований, входящих в неё.
/// </summary>
public sealed class CategoryDetailDto
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("display_order")]
    public int DisplayOrder { get; init; }

    [JsonPropertyName("competitions")]
    public List<CategoryCompetitionDto> Competitions { get; init; } = [];
}

/// <summary>
/// DTO одного соревнования в составе категории.
/// </summary>
public sealed class CategoryCompetitionDto
{
    /// <summary>Competition.Id</summary>
    [JsonPropertyName("id")]
    public int Id { get; init; }

    /// <summary>Competition.Name</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Competition.Date (dd/MM/yyyy)</summary>
    [JsonPropertyName("date")]
    public string Date { get; init; } = string.Empty;

    /// <summary>Competition.PoolType (25m / 50m)</summary>
    [JsonPropertyName("pool_type")]
    public string PoolType { get; init; } = string.Empty;

    [JsonPropertyName("is_masters")]
    public bool IsMasters { get; init; }

    [JsonPropertyName("is_award")]
    public bool IsAward { get; init; }

    [JsonPropertyName("show_combine_all_results")]
    public bool ShowCombineAllResults { get; init; }

    /// <summary>CategoryCompetition.DisplayOrder — порядок внутри категории.</summary>
    [JsonPropertyName("display_order")]
    public int DisplayOrder { get; init; }
}
