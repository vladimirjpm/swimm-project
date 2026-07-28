using System.Globalization;
using Swimm.Application.Dtos;

namespace Swimm.Application.Validation;

/// <summary>
/// Текстовое представление шкалы «место → очки» для админской формы правил очков.
/// Два формата ввода, один вывод:
///   • <c>30,28,26,…</c> — очки подряд с первого места (основной сценарий: скопировать
///     строку из регламента, не кликая 24 поля);
///   • <c>1=30</c> / <c>1:30</c> построчно — когда шкала с пропусками или начинается не с первого места.
/// Форматируется компактным списком, если места идут подряд с 1, иначе построчно.
/// </summary>
public static class PointRuleScaleText
{
    public static bool TryParse(string? text, out List<PointRuleEntryDto> entries, out string? error)
    {
        entries = [];
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return true;

        var tokens = text.Split([',', ';', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var place = 0;

        foreach (var token in tokens)
        {
            var parts = token.Split(['=', ':'], StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ||
                    !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pts))
                {
                    error = $"Не разобрать строку шкалы «{token}» — ожидается «место = очки»";
                    return false;
                }
                // Дальше нумерация продолжается от явно указанного места: смешанный ввод
                // «1=30, 28, 26» читается как 1,2,3, а не сбрасывается в начало.
                place = p;
                entries.Add(new PointRuleEntryDto { Place = p, Points = pts });
            }
            else if (parts.Length == 1)
            {
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pts))
                {
                    error = $"Не разобрать значение шкалы «{token}» — ожидается число очков";
                    return false;
                }
                place++;
                entries.Add(new PointRuleEntryDto { Place = place, Points = pts });
            }
            else
            {
                error = $"Не разобрать строку шкалы «{token}»";
                return false;
            }
        }

        var dup = entries.GroupBy(e => e.Place).FirstOrDefault(g => g.Count() > 1);
        if (dup != null)
        {
            error = $"Место {dup.Key} встречается в шкале дважды";
            return false;
        }

        entries = entries.OrderBy(e => e.Place).ToList();
        return true;
    }

    public static string Format(IReadOnlyList<PointRuleEntryDto> entries)
    {
        if (entries.Count == 0) return "";

        var ordered = entries.OrderBy(e => e.Place).ToList();
        var contiguous = ordered.Select((e, i) => e.Place == i + 1).All(x => x);

        return contiguous
            ? string.Join(", ", ordered.Select(e => e.Points))
            : string.Join("\n", ordered.Select(e => $"{e.Place} = {e.Points}"));
    }
}
