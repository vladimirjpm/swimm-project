using System.Text.Json;
using Swimm.Domain;

namespace Swimm.Application.Validation;

/// <summary>
/// Протокол, в котором КАЖДАЯ строка — неявка (NS/DNS): заявки напечатаны, а заплывов не было.
/// Так loglig экспортирует соревнование, которое не провели или по которому не внесли
/// результаты: «ליגה בוגרים 2 -קיץ 2025» (compID 14676, loglig 12637, 13.06.2025) — 273 строки
/// «0 NS», ни одного времени, а в таблице дисциплин на loglig «סה"כ משתתפים» = 0 у всех 10.
///
/// Для нас это то же, что пустой PDF: тянуть нечего. Без этого правила разбор «удавался»,
/// пометка «∅ нет протокола» снималась, а строка висела в «ждёт затягивания» с превью из
/// одних неявок. Импорт завёл бы соревнование без единого проплытого старта.
///
/// Неявка — по <see cref="StartStatus.IsNoStart"/>, единственному месту этого правила.
/// DQ и DNF сюда не относятся: пловец стартовал.
/// </summary>
public static class NoStartProtocol
{
    /// <summary>
    /// Неизменная часть сообщения. По ней <see cref="BulkPullClassifier"/> отличает «тянуть
    /// нечего» от сбоя — поэтому формулировку не меняют, не поправив и его.
    /// </summary>
    public const string Marker = "все строки — неявка";

    public static string Message(int rows) =>
        $"В протоколе {rows} строк, и {Marker} (NS/DNS): соревнование не проводилось или результаты не внесены — тянуть нечего";

    /// <summary>
    /// True — в разборе есть строки, и ни одна не стартовала. Пустой массив — false
    /// (это отдельный случай «парсер не распознал ни одного результата»).
    /// </summary>
    /// <param name="resultsJson"><c>ParsedCompetition.ResultsJson</c> — массив строк со snake_case полями.</param>
    public static bool AllNoStart(string? resultsJson)
    {
        if (string.IsNullOrWhiteSpace(resultsJson)) return false;

        using var doc = JsonDocument.Parse(resultsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return false;

        var any = false;
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            any = true;
            if (!StartStatus.IsNoStart(IsTrue(row, "time_fail"), Text(row, "time_fail_note")))
                return false;
        }
        return any;
    }

    // time_fail встречается и булевым, и строкой «"true"» (как is_masters в том же JSON).
    private static bool IsTrue(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var v) && v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) && b,
            _ => false,
        };

    private static string? Text(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
