using System.Text.RegularExpressions;
using Swimm.Application.Dtos;

namespace Swimm.Application.Validation;

/// <summary>
/// Табличка расхождения очков — вводом одной строкой: <c>21:5&gt;6, 22:3&gt;5</c>
/// («место : по регламенту &gt; начислено официально»), при желании с указанием, КОМУ
/// достались очки: <c>9:12&gt;25 | Maccabi Haifa</c>.
///
/// Тем же приёмом, что и шкала правила (<see cref="PointRuleScaleText"/>): админ переносит
/// пару чисел из протокола, а не заполняет сетку полей. Разделители строк — точка с запятой
/// или перевод строки; между числами допустимы <c>&gt;</c>, <c>-&gt;</c>, <c>→</c>.
///
/// Запятая тоже делит строки, но ТОЛЬКО пока в тексте нет «кто»: в названиях клубов и
/// именах она встречается («Cohen, Dan»), и резать по ней значило бы рвать их пополам.
/// </summary>
public static partial class ScaleDiffText
{
    [GeneratedRegex(@"^\s*(\d+)\s*[:=]\s*(\d+)\s*(?:->|>|→)\s*(\d+)\s*(?:\|\s*(.+?))?\s*$")]
    private static partial Regex RowRx();

    /// <summary>Разбор ввода. false — вернётся понятная ошибка для формы.</summary>
    public static bool TryParse(string? text, out List<ScaleDiffRowDto> rows, out string? error)
    {
        rows = [];
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return true;

        // «Кто» пишется после «|», и запятая внутри имени — обычное дело. Как только в тексте
        // появляется хоть один такой столбец, строки делим только по «;» и переводу строки.
        var separators = text.Contains('|') ? new[] { ';', '\n', '\r' } : [',', ';', '\n', '\r'];

        foreach (var chunk in text.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(chunk)) continue;

            var m = RowRx().Match(chunk);
            if (!m.Success)
            {
                error = $"Не разобрал «{chunk.Trim()}» — ожидается «место:по регламенту>начислено», " +
                        "например 21:5>6 или 9:12>25 | Maccabi Haifa";
                return false;
            }

            var place = int.Parse(m.Groups[1].Value);
            if (place <= 0)
            {
                error = "Место должно быть больше нуля";
                return false;
            }

            var subject = m.Groups[4].Success ? m.Groups[4].Value.Trim() : null;
            if (subject is { Length: > SubjectMaxLength })
            {
                error = $"«Кто» длиннее {SubjectMaxLength} символов — это подпись строки, а не абзац";
                return false;
            }

            rows.Add(new ScaleDiffRowDto(
                place, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                string.IsNullOrWhiteSpace(subject) ? null : subject));
        }

        if (rows.Select(r => r.Place).Distinct().Count() != rows.Count)
        {
            error = "Одно место указано дважды";
            return false;
        }

        rows = rows.OrderBy(r => r.Place).ToList();
        return true;
    }

    /// <summary>Предел длины подписи строки — та же величина, что у колонки в БД.</summary>
    public const int SubjectMaxLength = 120;

    /// <summary>
    /// Обратно в строку для формы. Разделитель зависит от того, есть ли «кто»: с ним строки
    /// уходят на отдельные линии, иначе форма вернула бы текст, который сама же не разберёт.
    /// </summary>
    public static string Format(IEnumerable<ScaleDiffRowDto> rows)
    {
        var ordered = rows.OrderBy(r => r.Place).ToList();
        var parts = ordered.Select(r => r.Subject is { Length: > 0 }
            ? $"{r.Place}:{r.Expected}>{r.Actual} | {r.Subject}"
            : $"{r.Place}:{r.Expected}>{r.Actual}");

        return string.Join(ordered.Any(r => r.Subject is { Length: > 0 }) ? "\n" : ", ", parts);
    }
}
