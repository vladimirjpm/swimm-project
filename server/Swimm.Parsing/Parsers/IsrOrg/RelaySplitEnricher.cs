using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Swimm.Parsing.Parsers.IsrOrg;

/// <summary>Одна эстафетная дисциплина из PDF промежуточных: стиль и дистанция в форме парсера.</summary>
/// <param name="Style">«freestyle» / «individual_medley».</param>
/// <param name="Len">«4X50», «4X100» — как <c>event_style_len</c> основного разбора.</param>
public sealed record RelaySplitEvent(string Style, string Len, IReadOnlyList<SplitRelayTeam> Teams);

/// <summary>Что сделала доклейка — для лога импорта и сообщения в админке.</summary>
public sealed record RelaySplitReport(
    int Teams, int Enriched, int NoLegsInSource, int Unmatched, int Ambiguous, int BirthYearMismatch)
{
    public override string ToString() =>
        $"промежуточные эстафет: команд {Teams}, доклеено {Enriched}"
        + (NoLegsInSource > 0 ? $", без ног в источнике {NoLegsInSource}" : "")
        + (Unmatched > 0 ? $", не нашлось в протоколе {Unmatched}" : "")
        + (Ambiguous > 0 ? $", неоднозначно {Ambiguous}" : "")
        + (BirthYearMismatch > 0 ? $", состав не сошёлся по годам {BirthYearMismatch}" : "");
}

/// <summary>
/// Доклейка промежуточных эстафет к РАЗОБРАННОМУ протоколу (JSON импорта), без переделки
/// основного парсера — docs/relays.md, «Промежуточные эстафет».
///
/// Ключ сопоставления: стиль + дистанция + заплыв + дорожка + итоговое время. Берётся только
/// однозначное совпадение: у мастерсов женская и смешанная комплексные эстафеты — один стиль и
/// одна дистанция, и заплыв/дорожка у них пересекаются; итоговое время их разводит.
///
/// Состав заменяется целиком (порядок плавания, имена из колонок источника, промежуточное
/// этапа, владелец строки = первая нога), но только если годы рождения ног совпали с тем, что уже разобрал основной парсер
/// (как мультимножество: порядок ног в основном протоколе не гарантирован). Не совпали —
/// строку не трогаем. Имена берём из PDF промежуточных, потому что основной протокол рвёт
/// длинные фамилии («גוסטמלסק»+«י» давало «קיגוסטמלס» — нового пловца-тень при импорте).
/// </summary>
public static class RelaySplitEnricher
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <param name="writeSplitTimes">
    /// false — состав и владельца строки ставим, а ВРЕМЕНА ЭТАПОВ не пишем: с 20.09.2026
    /// их пишет доклейка по строкам базы (<c>--attach-splits</c>, решение Влада Д7), а
    /// импорту остаётся только то, что меняет сами строки. Два писателя одного поля —
    /// ровно та ситуация, из которой выросли дубли 1581.
    /// </param>
    public static (string Json, RelaySplitReport Report) Apply(
        string resultsJson, IReadOnlyList<RelaySplitEvent> events, bool writeSplitTimes = true)
    {
        var root = JsonNode.Parse(resultsJson) as JsonArray
                   ?? throw new InvalidOperationException("Ожидался JSON-массив результатов.");

        var relayRows = root.OfType<JsonObject>()
            .Where(o => o["is_relay"]?.GetValue<bool?>() == true)
            .ToList();

        int teams = 0, enriched = 0, noLegs = 0, unmatched = 0, ambiguous = 0, mismatch = 0;

        foreach (var ev in events)
        foreach (var team in ev.Teams)
        {
            teams++;
            if (team.Legs.Count != 4 || team.Time is null) { noLegs++; continue; }

            var candidates = relayRows.Where(o =>
                    string.Equals(Str(o, "event_style_name"), ev.Style, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Str(o, "event_style_len"), ev.Len, StringComparison.OrdinalIgnoreCase)
                    && Int(o, "heat") == team.Heat
                    && Int(o, "lane") == team.Lane
                    && Str(o, "time") == team.Time)
                .ToList();

            if (candidates.Count == 0) { unmatched++; continue; }
            if (candidates.Count > 1) { ambiguous++; continue; }

            var row = candidates[0];
            if (row["relay_swimmers"] is JsonArray existing && existing.Count > 0)
            {
                var had = existing.OfType<JsonObject>().Select(l => Int(l, "birth_year") ?? 0).OrderBy(y => y);
                var got = team.Legs.Select(l => l.BirthYear ?? 0).OrderBy(y => y);
                if (!had.SequenceEqual(got)) { mismatch++; continue; }
            }

            row["relay_swimmers"] = new JsonArray(team.Legs.Select(l => (JsonNode)new JsonObject
            {
                ["order"] = l.Order,
                ["last_name"] = l.LastName,
                ["first_name"] = l.FirstName,
                ["birth_year"] = l.BirthYear,
                ["club"] = null,
                ["split_time"] = writeSplitTimes ? l.SplitTime : null,
            }).ToArray());
            row["relay_swimmers_name"] = string.Join(", ", team.Legs.Select(l => $"{l.FirstName} {l.LastName}".Trim()));
            // Владелец строки — первая нога (так строку собирает и основной парсер,
            // IsrOrgParser.CreateRelayResult). Иначе строка осталась бы за пловцом-тенью из
            // порванной фамилии, даже когда ноги уже привязаны правильно.
            var lead = team.Legs[0];
            row["last_name"] = lead.LastName;
            row["first_name"] = lead.FirstName;
            row["birth_year"] = lead.BirthYear;
            enriched++;
        }

        return (root.ToJsonString(WriteOptions),
            new RelaySplitReport(teams, enriched, noLegs, unmatched, ambiguous, mismatch));
    }

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? Int(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
}
