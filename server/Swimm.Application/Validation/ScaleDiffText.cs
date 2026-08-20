using System.Text.RegularExpressions;
using Swimm.Application.Dtos;

namespace Swimm.Application.Validation;

/// <summary>
/// Табличка расхождения очков — вводом одной строкой: <c>21:5&gt;6, 22:3&gt;5</c>
/// («место : по регламенту &gt; начислено официально»). После вертикальных черт идёт
/// контекст строки протокола, позиционно и весь необязательный:
/// <c>место:регламент&gt;официально | кому | заплыв | время</c>, например
/// <c>9:12&gt;25 | Maccabi Haifa | 1 | 32.90</c>.
///
/// Зачем контекст. Одни цифры доказывают расхождение только когда причина в самой шкале.
/// Если очки розданы по номеру заплыва (разбор 1581), то номер заплыва — и есть довод, а
/// время показывает, что места расставлены верно и спорны именно очки.
///
/// Тем же приёмом, что и шкала правила (<see cref="PointRuleScaleText"/>): админ переносит
/// строки из протокола, а не заполняет сетку полей. Разделители строк — точка с запятой
/// или перевод строки; между числами допустимы <c>&gt;</c>, <c>-&gt;</c>, <c>→</c>.
///
/// Запятая тоже делит строки, но ТОЛЬКО пока в тексте нет контекста: в названиях клубов и
/// именах она встречается («Cohen, Dan»), и резать по ней значило бы рвать их пополам.
/// </summary>
public static partial class ScaleDiffText
{
    [GeneratedRegex(@"^\s*(\d+)\s*[:=]\s*(\d+)\s*(?:->|>|→)\s*(\d+)\s*(?:\|(.*))?$")]
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

            // Контекст позиционный: кому | заплыв | время. Пропущенное поле — пустая ячейка
            // между чертами, чтобы «время без заплыва» не съезжало в соседнюю колонку.
            var context = m.Groups[4].Success
                ? m.Groups[4].Value.Split('|').Select(p => p.Trim()).ToArray()
                : [];

            var subject = Cell(context, 0);
            if (subject is { Length: > SubjectMaxLength })
            {
                error = $"«Кто» длиннее {SubjectMaxLength} символов — это подпись строки, а не абзац";
                return false;
            }

            int? heat = null;
            if (Cell(context, 1) is { } heatText)
            {
                if (!int.TryParse(heatText, out var heatValue) || heatValue <= 0)
                {
                    error = $"Не разобрал заплыв «{heatText}» — ожидается положительное число";
                    return false;
                }
                heat = heatValue;
            }

            rows.Add(new ScaleDiffRowDto(
                place, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                subject, heat, Cell(context, 2)));
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

    /// <summary>Ячейка контекста по позиции; пустая («9:12>25 || 1») — как отсутствующая.</summary>
    private static string? Cell(string[] context, int index) =>
        index < context.Length && context[index].Length > 0 ? context[index] : null;

    /// <summary>
    /// Обратно в строку для формы. Разделитель зависит от того, есть ли контекст: с ним строки
    /// уходят на отдельные линии, иначе форма вернула бы текст, который сама же не разберёт.
    ///
    /// Хвостовые пустые ячейки обрезаются, а срединные остаются: «время без заплыва» обязано
    /// сохранить свою позицию, иначе разбор вернул бы его в колонку заплыва.
    /// </summary>
    public static string Format(IEnumerable<ScaleDiffRowDto> rows)
    {
        var ordered = rows.OrderBy(r => r.Place).ToList();
        var parts = ordered.Select(r =>
        {
            var head = $"{r.Place}:{r.Expected}>{r.Actual}";
            var context = new[] { r.Subject, r.Heat?.ToString(), r.Time };

            var last = Array.FindLastIndex(context, c => !string.IsNullOrWhiteSpace(c));
            return last < 0
                ? head
                : $"{head} | {string.Join(" | ", context.Take(last + 1).Select(c => c ?? string.Empty))}";
        });

        return string.Join(ordered.Any(HasContext) ? "\n" : ", ", parts);
    }

    private static bool HasContext(ScaleDiffRowDto row) =>
        row.Subject is { Length: > 0 } || row.Heat is not null || row.Time is { Length: > 0 };
}
