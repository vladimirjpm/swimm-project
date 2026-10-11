using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Импорт: даты дисциплин многодневки из пособытийного источника вместо одной даты протокола
/// (docs/plans/multiday-split-plan.md, М3).
///
/// PDF loglig печатает в шапке только диапазон («03/01/2025 - 04/01/2025»), и разбор кладёт все
/// строки на одну дату. Без этого шага перезалив разделённого соревнования (#1645 → дни #1675 и
/// #1645) положил бы ВСЕ строки во второй день — дубли заплывов первого дня.
///
/// Трогаем только явный симптом: в протоколе одна дата, а у источника дней больше. Протокол, где
/// разбор сам нашёл разные даты, не переписываем. Всё или ничего: хоть одна строка без
/// однозначного дня — даты остаются как были, причина уходит в предупреждения.
/// </summary>
public static class CollapsedDaysRewriter
{
    private const string DateFormat = "dd/MM/yyyy";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Итог: переписан ли протокол и почему.</summary>
    /// <param name="Warn">Сказать человеку в превью: даты переписаны или переписать не вышло.
    /// false — делить нечего (обычный однодневный старт), молчим.</param>
    public sealed record Outcome(ParsedCompetition Parsed, bool Rewritten, string Message, bool Warn = false);

    public static Outcome Rewrite(ParsedCompetition parsed, DisciplineDates dates)
    {
        if (JsonNode.Parse(parsed.ResultsJson) is not JsonArray root)
            return new Outcome(parsed, false, "даты дисциплин: протокол не JSON-массив");

        var rows = root.OfType<JsonObject>().ToList();
        var fileDates = rows.Select(r => (string?)r["date"]).Distinct().ToList();
        if (fileDates.Count != 1)
            return new Outcome(parsed, false, $"даты дисциплин: в протоколе уже {fileDates.Count} дат — не трогаем");
        if (dates.Days.Count < 2)
            return new Outcome(parsed, false, "даты дисциплин: у источника один день");

        var plan = DisciplineDayPlanner.Plan(
            rows.Select((r, i) => new DayPlanRow(
                i, (string?)r["event_style_name"] ?? "", (string?)r["event_style_len"] ?? "",
                (string?)r["event_style_gender"] ?? "")),
            dates);
        if (!plan.CanApply)
            return new Outcome(parsed, false,
                $"даты дисциплин НЕ переписаны: у {plan.Unresolved.Values.Sum()} строк нет однозначного дня "
                + $"({string.Join(", ", plan.Unresolved.Keys.Take(5))}) — протокол остался одной датой {fileDates[0]}", Warn: true);

        foreach (var (day, indexes) in plan.ByDay)
        {
            var text = day.ToString(DateFormat, CultureInfo.InvariantCulture);
            foreach (var i in indexes) rows[(int)i]["date"] = text;
        }

        var summaries = rows
            .GroupBy(r => ((string?)r["competition"] ?? "", (string?)r["date"] ?? "", (string?)r["pool_type"]))
            .Select(g => new ParsedCompetitionSummary(g.Key.Item1, g.Key.Item2, g.Count(), g.Key.Item3))
            .OrderBy(s => DateOnly.TryParseExact(s.Date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d : DateOnly.MaxValue)
            .ToList();

        var message = "даты дисциплин из сетки loglig: "
                      + string.Join(", ", plan.ByDay.Select(kv => $"{kv.Key.ToString(DateFormat, CultureInfo.InvariantCulture)} — {kv.Value.Count} строк"));
        return new Outcome(
            parsed with { ResultsJson = root.ToJsonString(WriteOptions), Competitions = summaries },
            true, message, Warn: true);
    }
}
