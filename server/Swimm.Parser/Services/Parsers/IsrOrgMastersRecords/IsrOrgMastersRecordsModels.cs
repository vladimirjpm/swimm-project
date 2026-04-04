using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Swimm.Parser.Services.Parsers.IsrOrgMastersRecords;

/// <summary>
/// Запись рекорда мастерс из таблицы ISR.
/// </summary>
public record MastersRecord(
    string StyleName,       // normalized English style name (e.g. "butterfly")
    string Distance,        // e.g. "50"
    string Gender,          // "male" / "female"
    string AgeGroup,        // e.g. "25-29", "30-34"
    string Time,            // e.g. "00:25.81"
    string RecordDate,      // e.g. "09/07/2010"
    string SwimmerName,     // full name (Hebrew)
    string Club,            // club name (Hebrew)
    string Pool,            // pool info
    string PoolType,        // "50m" or "25m"
    bool IsRelay
);

/// <summary>
/// Лист-запись нормативного словаря мастерс рекордов.
/// </summary>
public record MastersNormativeEntry(
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("club")] string Club,
    [property: JsonPropertyName("record_date")] string RecordDate
);

/// <summary>
/// Обёртка нормативного вывода:
/// { lastUpdate_50m: "06/04/2025", lastUpdate_25m: "06/04/2025", normatives: { male: { 50m_pool: { butterfly: { 50m: { "25-29": {...} } } } } } }
/// </summary>
public record MastersNormativeOutput(
    [property: JsonPropertyName("lastUpdate_50m")]
    string LastUpdate50m,

    [property: JsonPropertyName("lastUpdate_25m")]
    string LastUpdate25m,

    [property: JsonPropertyName("normatives")]
    Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, MastersNormativeEntry>>>>> Normatives
);
