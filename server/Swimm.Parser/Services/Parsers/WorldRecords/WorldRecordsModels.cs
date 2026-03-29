using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Swimm.Parser.Services.Parsers.WorldRecords;

/// <summary>
/// Лист-запись нормативного словаря мировых рекордов.
/// Хранит время, имя спортсмена, страну и дату рекорда.
/// </summary>
public record WorldRecordEntry(
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("country")] string Country,
    [property: JsonPropertyName("record_date")] string RecordDate
);

/// <summary>
/// Обёртка нормативного вывода:
/// { lastUpdate: "15/12/2024", normatives: { gender: { pool_type: { style: { distance: { ISR/WR: {...} } } } } } }
/// </summary>
public record WorldRecordsNormativeOutput(
    [property: JsonPropertyName("lastUpdate")]
    string LastUpdate,

    [property: JsonPropertyName("normatives")]
    Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, WorldRecordEntry>>>>> Normatives
);
