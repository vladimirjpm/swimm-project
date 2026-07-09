using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>
/// Плоский DTO рекорда для публичного API (/api/records). Клиентский RecordsHelper
/// пересобирает из плоского списка легаси-структуру window.normative_*_record.
/// </summary>
public class RecordDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>world | continent | country.</summary>
    [JsonPropertyName("region_type")]
    public string RegionType { get; set; } = string.Empty;

    /// <summary>"" | EU/AS | ISO-код страны.</summary>
    [JsonPropertyName("region_code")]
    public string RegionCode { get; set; } = string.Empty;

    /// <summary>open | age | junior | masters.</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("age_key")]
    public string AgeKey { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = string.Empty;

    [JsonPropertyName("pool_type")]
    public string PoolType { get; set; } = string.Empty;

    [JsonPropertyName("style")]
    public string Style { get; set; } = string.Empty;

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    [JsonPropertyName("holder_name")]
    public string? HolderName { get; set; }

    [JsonPropertyName("club")]
    public string? Club { get; set; }

    [JsonPropertyName("holder_country")]
    public string? HolderCountry { get; set; }

    [JsonPropertyName("record_date")]
    public string? RecordDate { get; set; }
}

/// <summary>Плоский DTO норматива для /api/normative-standards.</summary>
public class NormativeStandardDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>regular | masters.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Система нормативов (сейчас "RUS").</summary>
    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = string.Empty;

    [JsonPropertyName("pool_type")]
    public string PoolType { get; set; } = string.Empty;

    [JsonPropertyName("style")]
    public string Style { get; set; } = string.Empty;

    [JsonPropertyName("distance")]
    public string Distance { get; set; } = string.Empty;

    [JsonPropertyName("age_key")]
    public string AgeKey { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;
}
