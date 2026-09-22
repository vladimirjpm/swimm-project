using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Swimm.Parsing.Parsers.IsrOrg;

/// <summary>Эстафетная дисциплина loglig для сборки целиком: сетка программы + PDF промежуточных.</summary>
/// <param name="Style">Стиль в форме основного парсера («freestyle» / «individual_medley»).</param>
/// <param name="Len">«4X50», «4X100».</param>
/// <param name="Gender">«female» / «male» / «none» (микст) — из сетки loglig.</param>
/// <param name="EventName">Название дисциплины как в сетке («4X50 מעורב שליחים»).</param>
public sealed record RelayBuildEvent(string Style, string Len, string Gender, string EventName, IReadOnlyList<SplitRelayTeam> Teams);

/// <summary>
/// Этап 2 (docs/plans/relay-splits-masters-plan.md): эстафеты мастерс-чемпионата строятся
/// ЦЕЛИКОМ из пособытийных данных loglig, а эстафетные строки основного разбора выбрасываются.
///
/// Зачем: общий PDF печатает мастерские эстафеты секциями без стиля и пола, и основной парсер
/// теряет часть дисциплин (зимний чемпионат 2026: 79 команд из 124, микст целиком), места
/// ставит сквозные, категорию — одну на всех. У пособытийного источника всё это есть.
///
/// Решения Влада 19.09.2026 (по рекомендациям):
/// - пол: женские/мужские — <c>female</c>/<c>male</c> (как детские полосы
///   <see cref="RelayBandReconstructor"/>), микст — <c>none</c> (конвенция «mix → none»);
/// - категория — полоса по СУММЕ возрастов: <c>160-199</c>, у микста <c>mix-160-199</c>
///   (формат <c>NormalizeEventCategory</c>); <c>age_group</c> — та же полоса;
///   <c>event_style_age</c> — нижняя граница полосы ЧИСЛОМ (клиентский isResultMasters
///   требует число ≥ 25), а не возраст первой ноги, как было;
/// - только мастерс-чемпионаты: признак — строка полосы у КАЖДОЙ команды (её печатают
///   только мастерские эстафеты).
/// Место — внутри «дисциплина × пол × полоса» по времени; равные времена делят место;
/// DQ/NS без места.
///
/// Всё или ничего: у хоть одной команды нет полосы или четырёх ног — <c>null</c>, и вызывающий
/// остаётся на доклейке к основному разбору (этап 1).
/// </summary>
public static class RelayMastersBuilder
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Годится ли источник для сборки целиком (мастерский формат, полный состав).</summary>
    public static bool CanBuild(IReadOnlyList<RelayBuildEvent> events) =>
        events.Count > 0
        && events.All(e => e.Teams.Count > 0)
        && events.SelectMany(e => e.Teams).All(t => t.Band != null && t.Legs.Count == 4
                                                    && (t.Time != null || t.Status != null));

    /// <returns>Новый JSON и число эстафет; null — источник не годится (см. <see cref="CanBuild"/>).</returns>
    public static (string Json, int Relays, int Replaced)? Build(string resultsJson, IReadOnlyList<RelayBuildEvent> events)
    {
        if (!CanBuild(events)) return null;

        var root = JsonNode.Parse(resultsJson) as JsonArray
                   ?? throw new InvalidOperationException("Ожидался JSON-массив результатов.");

        var rows = root.OfType<JsonObject>().ToList();
        var oldRelays = rows.Where(IsRelay).ToList();
        // Поля соревнования (название, дата, страна, бассейн, мастерс) — из строки этого же
        // файла: эстафетная надёжнее (у неё та же дата, что у эстафет), иначе любая.
        var template = oldRelays.FirstOrDefault() ?? rows.FirstOrDefault()
                       ?? throw new InvalidOperationException("В разборе нет ни одной строки.");

        var built = new List<JsonObject>();
        foreach (var ev in events)
        foreach (var band in ev.Teams.GroupBy(t => t.Band!))
        {
            var places = Places(band.ToList());
            foreach (var team in band)
                built.Add(Row(template, ev, team, places.GetValueOrDefault(team)));
        }

        foreach (var old in oldRelays) root.Remove(old);
        foreach (var row in built) root.Add(row);

        return (root.ToJsonString(WriteOptions), built.Count, oldRelays.Count);
    }

    private static Dictionary<SplitRelayTeam, int> Places(List<SplitRelayTeam> teams)
    {
        var timed = teams
            .Where(t => t.Time != null)
            .Select(t => (Team: t, Ms: ToMs(t.Time!)))
            .OrderBy(x => x.Ms)
            .ToList();

        var places = new Dictionary<SplitRelayTeam, int>();
        for (var i = 0; i < timed.Count; i++)
            places[timed[i].Team] = i > 0 && timed[i].Ms == timed[i - 1].Ms
                ? places[timed[i - 1].Team]
                : i + 1;
        return places;
    }

    private static JsonObject Row(JsonObject template, RelayBuildEvent ev, SplitRelayTeam team, int place)
    {
        var row = (JsonObject)template.DeepClone();
        var band = team.Band!;
        var lead = team.Legs[0];
        var mixed = ev.Gender is not ("female" or "male");

        row["event"] = ev.EventName;
        row["event_style_name"] = ev.Style;
        row["event_style_len"] = ev.Len;
        row["event_style_gender"] = mixed ? "mixed" : ev.Gender; // Э4: смешанная — не «неизвестен»
        row["age_group"] = band;
        row["event_style_age"] = band.Split('-')[0];
        row["event_category"] = mixed ? $"mix-{band}" : band;
        row["position"] = place > 0 ? place : null;
        row["heat"] = team.Heat;
        row["lane"] = team.Lane;
        row["last_name"] = lead.LastName;
        row["first_name"] = lead.FirstName;
        row["last_name_en"] = "";
        row["first_name_en"] = "";
        row["birth_year"] = lead.BirthYear;
        row["club"] = team.Club;
        row["club_en"] = "";
        row["time"] = team.Time ?? "";
        row["time_fail"] = team.Time == null;
        row["time_fail_note"] = team.Time == null ? team.Status : null;
        row["international_points"] = 0;
        row["note"] = null;
        row["is_relay"] = true;
        row["relay_team_name"] = team.Club;
        row["relay_swimmers_name"] = string.Join(", ", team.Legs.Select(l => $"{l.FirstName} {l.LastName}".Trim()));
        row["relay_swimmers"] = new JsonArray(team.Legs.Select(l => (JsonNode)new JsonObject
        {
            ["order"] = l.Order,
            ["last_name"] = l.LastName,
            ["first_name"] = l.FirstName,
            ["birth_year"] = l.BirthYear,
            ["club"] = null,
            ["split_time"] = l.SplitTime,
        }).ToArray());
        row["heat_type"] = null;
        row["round"] = null;
        row["official_club_points"] = null;
        return row;
    }

    private static bool IsRelay(JsonObject o) =>
        o["is_relay"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    /// <summary>«02:34.67» / «34.67» → мс; неразборчивое — в конец списка.</summary>
    private static long ToMs(string time)
    {
        var parts = time.Split(':');
        double seconds = 0;
        foreach (var p in parts)
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return long.MaxValue;
            seconds = seconds * 60 + v;
        }
        return (long)Math.Round(seconds * 1000);
    }
}
