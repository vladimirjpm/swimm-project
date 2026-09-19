using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Swimm.Parsing.Parsers.IsrOrg;

/// <summary>Личная дисциплина из PDF промежуточных: стиль, дистанция и пол в форме парсера.</summary>
public sealed record IndividualSplitEvent(string Style, string Len, string Gender, IReadOnlyList<SplitSwim> Swims);

/// <summary>
/// Промежуточные ЛИЧНЫХ заплывов (<c>time_split</c>, «31.52;34.84» — формат, который уже
/// понимает клиентская ячейка времени) к разобранному протоколу. Ключ: стиль + дистанция + пол
/// + итоговое время + год рождения; только однозначное совпадение. Остальное строки не трогает.
/// </summary>
public static class IndividualSplitEnricher
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static (string Json, int Swims, int Enriched) Apply(string resultsJson, IReadOnlyList<IndividualSplitEvent> events)
    {
        var root = JsonNode.Parse(resultsJson) as JsonArray
                   ?? throw new InvalidOperationException("Ожидался JSON-массив результатов.");

        var rows = root.OfType<JsonObject>()
            .Where(o => !(o["is_relay"] is JsonValue v && v.TryGetValue<bool>(out var b) && b))
            .ToList();

        int swims = 0, enriched = 0;
        foreach (var ev in events)
        foreach (var swim in ev.Swims)
        {
            swims++;
            var hits = rows.Where(o =>
                    string.Equals(Str(o, "event_style_name"), ev.Style, StringComparison.OrdinalIgnoreCase)
                    && Str(o, "event_style_len") == ev.Len
                    && string.Equals(Str(o, "event_style_gender"), ev.Gender, StringComparison.OrdinalIgnoreCase)
                    && Str(o, "time") == swim.Time
                    && Int(o, "birth_year") == swim.BirthYear)
                .ToList();
            if (hits.Count != 1) continue;

            hits[0]["time_split"] = string.Join(";", swim.Laps);
            enriched++;
        }

        return (root.ToJsonString(WriteOptions), swims, enriched);
    }

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? Int(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
}
