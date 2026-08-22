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

    /// <summary>
    /// Открытая претензия к этой записи справочника (<c>Sys_RecordIssues</c>): код причины,
    /// null — запись не оспаривается. Ошибку источника мы не правим, а помечаем
    /// (docs/plans/records-quality-plan.md).
    /// </summary>
    [JsonPropertyName("issue_reason")]
    public string? IssueReason { get; set; }

    [JsonPropertyName("holder_name")]
    public string? HolderName { get; set; }

    [JsonPropertyName("club")]
    public string? Club { get; set; }

    [JsonPropertyName("holder_country")]
    public string? HolderCountry { get; set; }

    [JsonPropertyName("record_date")]
    public string? RecordDate { get; set; }

    /// <summary>Когда МЫ обновили эту запись (UTC) — не дата установления рекорда.</summary>
    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
    /// <summary>
    /// Год рождения держателя — ТОЛЬКО при включённой отладочной опции
    /// <c>ShowAgeRecordsDetails</c>, иначе null (см. DebugOption). В самом справочнике года
    /// рождения нет: он восстанавливается по нашим протоколам.
    /// </summary>
    [JsonPropertyName("holder_birth_year")]
    public int? HolderBirthYear { get; set; }

    /// <summary>
    /// Сколько держателю было в год рекорда («год рекорда − год рождения») — та ось, по
    /// которой федерация раскладывает ступени (docs/data-integrity.md §13). Ради этого числа
    /// подробности и включают: видно, почему запись стоит в ступени 10.
    /// </summary>
    [JsonPropertyName("holder_age")]
    public int? HolderAge { get; set; }

    /// <summary>
    /// Как опознан держатель: <c>verified</c> — сверка нашла его заплыв в наших протоколах;
    /// <c>name</c> — совпало уникальное имя. null — не опознан, год рождения неизвестен.
    /// </summary>
    [JsonPropertyName("holder_source")]
    public string? HolderSource { get; set; }
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
