using System.Text.Json;
using Swimm.Domain;

namespace Swimm.Application.Mapping;

/// <summary>Минимальная проекция строки разбора протокола — только то, что нужно для «проплыто ли».</summary>
/// <param name="Event">Заголовок события протокола (дисциплина × секция пола/возраста).</param>
/// <param name="Heat">Номер заплыва; 0 — не указан.</param>
/// <param name="Printed">Время как напечатано; пусто/«0:00.00» — времени нет.</param>
/// <param name="TimeFail">Признак парсера time_fail (NS/DNS/DQ… или строка без времени).</param>
/// <param name="FailNote">Что напечатано вместо времени (time_fail_note): «DNS», «DQ / SW 6.5», «NS»…
/// Оба производителя строк (<see cref="CompetitionProgress.EvaluateJson"/> и импорт) его передают.</param>
public sealed record ProgressRow(string Event, bool IsRelay, int Heat, string? Printed, bool TimeFail, string? FailNote = null)
{
    /// <summary>Есть настоящее время: не time_fail и в строке есть ненулевая цифра (как в day-json.mjs).</summary>
    public bool HasTime => !TimeFail && Printed is not null && Printed.Any(c => c is >= '1' and <= '9');

    /// <summary>
    /// Вместо времени — отметка судьи (DNS/DQ/DNF, <see cref="StartStatus.IsOfficialMark"/>):
    /// времени по строке не будет, ждать её нечего. «NS» и пустая графа — не отметка.
    /// </summary>
    public bool HasOfficialMark => StartStatus.IsOfficialMark(TimeFail, FailNote);
}

/// <summary>Сводка по одному событию протокола.</summary>
public sealed record EventProgress(string Event, bool IsRelay, int Rows, int Timed, bool Finished)
{
    /// <summary>Причина «ждёт» для человека; null — событие проплыто.</summary>
    public string? PendingReason => Finished ? null
        : Timed == 0 ? $"{Event}: ещё не плыли (0 из {Rows} со временем)"
        : IsRelay ? $"{Event}: идёт — в последнем заплыве эстафеты времён нет ({Timed} из {Rows} команд со временем)"
        : $"{Event}: идёт — в последнем заплыве времён нет ({Timed} из {Rows} со временем)";
}

/// <summary>Итог: закончено ли соревнование и что ещё ждёт.</summary>
public sealed record CompetitionProgressSummary(IReadOnlyList<EventProgress> Events)
{
    /// <summary>Хотя бы одно событие есть и все проплыты. Пустой разбор — не закончено.</summary>
    public bool IsFinished => Events.Count > 0 && Events.All(e => e.Finished);

    public IReadOnlyList<string> Pending => Events.Where(e => !e.Finished).Select(e => e.PendingReason!).ToList();

    /// <summary>Сколько причин перечислять поимённо; остальные — «и ещё N».</summary>
    public const int ListedPending = 3;

    /// <summary>
    /// Что ждёт, строками для человека: «ждут N из M событий», до <see cref="ListedPending"/> причин,
    /// «и ещё K». ОДИН текст на results-auto (причина «ещё идёт») и отметку «результаты неполные»
    /// у соревнования (<see cref="PartialResultsRule"/>). Пусто — всё проплыто.
    /// </summary>
    public IReadOnlyList<string> PendingLines()
    {
        if (IsFinished) return [];
        if (Events.Count == 0) return ["в разборе нет ни одного события"];
        var pending = Pending;
        var lines = new List<string> { $"ждут {pending.Count} из {Events.Count} событий" };
        lines.AddRange(pending.Take(ListedPending));
        if (pending.Count > ListedPending) lines.Add($"и ещё {pending.Count - ListedPending}");
        return lines;
    }

    /// <summary>
    /// То же по-английски для витрины (видимый UI сайта только English): «Waiting for 2 of 16
    /// events: A; B». Названия событий — как напечатаны в протоколе (это данные). null — всё проплыто.
    /// </summary>
    public string? PendingTextEn()
    {
        if (IsFinished) return null;
        if (Events.Count == 0) return "No events in the published results yet";
        var names = Events.Where(e => !e.Finished).Select(e => e.Event.Trim()).ToList();
        var listed = string.Join("; ", names.Take(ListedPending));
        var more = names.Count > ListedPending ? $"; and {names.Count - ListedPending} more" : "";
        var noun = Events.Count == 1 ? "event" : "events";
        return $"Waiting for {names.Count} of {Events.Count} {noun}: {listed}{more}";
    }
}

/// <summary>
/// «Какие события уже проплыты / закончено ли соревнование» — чистая функция без I/O.
/// Перенос логики из server/db/day-json.mjs (docs/plans/prod-auto-pull-plan.md §4.4, гейт
/// «Соревнование закончено» задачи results-auto). PDF loglig у идущего старта печатает ВСЕ
/// события, непроплытые — строками без времени.
///
/// Правило: событие проплыто, если в его ПОСЛЕДНЕМ заплыве (максимальный Heat) есть хоть одно
/// время — эстафета с проплытым только первым заплывом проплытой не считается.
///
/// Р120 (23.11.2026): строки с отметкой судьи (DNS/DQ/DNF — <see cref="ProgressRow.HasOfficialMark"/>)
/// в этом правиле не участвуют — по ним ждать нечего. «Последний заплыв» — последний среди
/// остальных строк; если остальных нет (секция целиком из DNS/DQ/DNF), событие проплыто.
/// Смешанный заплыв — часть строк с отметкой, часть пустые («NS» или ничего), времени нет —
/// по-прежнему не проплыт: пустая строка значит «результат не внесён», а DNS организатор может
/// проставить и заранее, до заплыва. Правило только снимает ложное «ждёт» и нового «ждёт» не
/// даёт: событие со временем в последнем заплыве проплыто, как и раньше.
/// Протокол, где неявка — каждая строка старта, — «∅ нет протокола» (Р116,
/// <see cref="Validation.NoStartProtocol"/>): results-auto отсекает его раньше этой оценки.
/// </summary>
public static class CompetitionProgress
{
    public static CompetitionProgressSummary Evaluate(IEnumerable<ProgressRow> rows)
    {
        var events = rows
            .GroupBy(r => r.Event)
            .Select(g =>
            {
                var list = g.ToList();
                var open = list.Where(r => !r.HasOfficialMark).ToList();
                var finished = open.Count == 0
                    || open.Any(r => r.Heat == open.Max(o => o.Heat) && r.HasTime);
                return new EventProgress(
                    g.Key,
                    list.Any(r => r.IsRelay),
                    list.Count,
                    list.Count(r => r.HasTime),
                    finished);
            })
            .ToList();
        return new CompetitionProgressSummary(events);
    }

    /// <summary>
    /// Та же оценка прямо по JSON разбора (<see cref="Dtos.ParsedCompetition.ResultsJson"/>:
    /// массив объектов snake_case). Нужна, потому что типа Result из Swimm.Parsing слой
    /// Application видеть не может (зависимость обратная).
    /// </summary>
    public static CompetitionProgressSummary EvaluateJson(string resultsJson)
    {
        using var doc = JsonDocument.Parse(resultsJson);
        var rows = new List<ProgressRow>();
        foreach (var o in doc.RootElement.EnumerateArray())
        {
            rows.Add(new ProgressRow(
                Str(o, "event") ?? "",
                o.TryGetProperty("is_relay", out var r) && r.ValueKind == JsonValueKind.True,
                o.TryGetProperty("heat", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt32() : 0,
                Str(o, "time"),
                IsTrue(o, "time_fail"),
                Str(o, "time_fail_note")));
        }
        return Evaluate(rows);
    }

    // time_fail встречается и булевым, и строкой «"true"» (как в NoStartProtocol).
    private static bool IsTrue(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) && b,
            _ => false,
        };

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
