using System.Collections.Generic;
using System.Text.Json.Serialization;
using Swimm.Parser.Services.Models;

namespace Swimm.Parser.Services.Parsers.IsrOrgAgeRecords;

/// <summary>
/// Parsed individual age record entry (one row from the records table).
/// </summary>
public record AgeRecord(
    string StyleName,       // normalized English style name (e.g. "freestyle")
    string Distance,        // e.g. "50", "100", "4X50"
    string Gender,          // "male" / "female" / "none"
    string AgeCategory,     // "israel" for national, or age like "18", "14", "10"
    string Time,            // e.g. "00:21.08"
    string Date,            // e.g. "27/12/2019"
    string SwimmerName,     // full name (Hebrew)
    string Club,            // club name (Hebrew)
    string Venue,           // venue / location
    bool IsRelay,
    string? RelaySwimmersInline  // comma-separated swimmer names for relays
);

/// <summary>
/// Leaf entry in the normative_record nested dictionary.
/// </summary>
public record NormativeEntry(
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("club")] string Club,
    [property: JsonPropertyName("country")] string Country,
    [property: JsonPropertyName("record_date")] string RecordDate
);

/// <summary>
/// Wrapper for the normative output: { normatives: { male: { 25m_pool: { freestyle: { 50m: { ISR: {...}, 18: {...} } } } } } }
/// </summary>
public record NormativeOutput(
    [property: JsonPropertyName("normatives")]
    Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, NormativeEntry>>>>> Normatives
);
