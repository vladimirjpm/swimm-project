using System.Text.RegularExpressions;
using Swimm.Application.Dtos;

namespace Swimm.Application.Validation;

/// <summary>
/// Табличка расхождения очков — вводом одной строкой: <c>21:5&gt;6, 22:3&gt;5</c>
/// («место : по регламенту &gt; начислено официально»).
///
/// Тем же приёмом, что и шкала правила (<see cref="PointRuleScaleText"/>): админ переносит
/// пару чисел из протокола, а не заполняет сетку полей. Разделители строк — запятая,
/// точка с запятой или перевод строки; между числами допустимы <c>&gt;</c>, <c>-&gt;</c>, <c>→</c>.
/// </summary>
public static partial class ScaleDiffText
{
    [GeneratedRegex(@"^\s*(\d+)\s*[:=]\s*(\d+)\s*(?:->|>|→)\s*(\d+)\s*$")]
    private static partial Regex RowRx();

    /// <summary>Разбор ввода. false — вернётся понятная ошибка для формы.</summary>
    public static bool TryParse(string? text, out List<ScaleDiffRowDto> rows, out string? error)
    {
        rows = [];
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return true;

        foreach (var chunk in text.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(chunk)) continue;

            var m = RowRx().Match(chunk);
            if (!m.Success)
            {
                error = $"Не разобрал «{chunk.Trim()}» — ожидается «место:по регламенту>начислено», например 21:5>6";
                return false;
            }

            var place = int.Parse(m.Groups[1].Value);
            if (place <= 0)
            {
                error = "Место должно быть больше нуля";
                return false;
            }

            rows.Add(new ScaleDiffRowDto(place, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)));
        }

        if (rows.Select(r => r.Place).Distinct().Count() != rows.Count)
        {
            error = "Одно место указано дважды";
            return false;
        }

        rows = rows.OrderBy(r => r.Place).ToList();
        return true;
    }

    /// <summary>Обратно в строку для формы.</summary>
    public static string Format(IEnumerable<ScaleDiffRowDto> rows) =>
        string.Join(", ", rows.OrderBy(r => r.Place).Select(r => $"{r.Place}:{r.Expected}>{r.Actual}"));
}
