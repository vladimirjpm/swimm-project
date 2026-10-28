using System.Globalization;
using System.Text.RegularExpressions;
using Swimm.Application.Dtos;

namespace Swimm.Application.Validation;

/// <summary>
/// Текстовый ввод полос и призов награды серии в админке (/Admin/Series) — по образцу
/// <see cref="PointRuleScaleText"/>: регламент переписывают строкой, а не кликают поля.
///
/// Полосы — через «;» или построчно: <c>F 10-11</c>, <c>M 13-14</c>, <c>F 16+</c>, <c>10-12</c>
/// (оба пола), <c>M -12</c>, <c>F 14</c> (ровно 14). F/Ж/female — девушки, M/М/male — юноши.
/// Призы — построчно (в тексте приза бывают запятые и «;»): <c>1: 1,000 ₪ + Cup</c>.
/// </summary>
public static partial class SeriesAwardText
{
    [GeneratedRegex(@"^(?:(?<g>[A-Za-zА-Яа-я]+)\s+)?(?:(?<min>\d+)\s*\+|(?<min>\d+)?\s*[-–]\s*(?<max>\d+)|(?<one>\d+))$")]
    private static partial Regex BandRx();

    public static bool TryParseBands(string? text, out List<SeriesAwardBandDto> bands, out string? error)
    {
        bands = [];
        error = null;
        foreach (var token in (text ?? "").Split([';', '\n', '\r'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = BandRx().Match(token);
            string? gender = null;
            if (m.Success && m.Groups["g"].Success && !TryGender(m.Groups["g"].Value, out gender))
                m = Match.Empty;
            if (!m.Success)
            {
                error = $"Не разобрать полосу «{token}» — ожидается, например, «F 10-11», «M 16+», «10-12»";
                return false;
            }

            int? min = Int(m.Groups["min"]), max = Int(m.Groups["max"]);
            if (Int(m.Groups["one"]) is int one) min = max = one;
            if (min is int a && max is int b && a > b)
            {
                error = $"В полосе «{token}» нижняя граница больше верхней";
                return false;
            }
            bands.Add(new SeriesAwardBandDto(gender, min, max));
        }
        return true;
    }

    public static string FormatBands(IEnumerable<SeriesAwardBandDto> bands) =>
        string.Join("; ", bands.Select(b =>
        {
            var range = (b.MinSeasonAge, b.MaxSeasonAge) switch
            {
                (int a, int c) when a == c => $"{a}",
                (int a, int c) => $"{a}-{c}",
                (int a, null) => $"{a}+",
                (null, int c) => $"-{c}",
                _ => "0+"
            };
            return b.Gender switch { "female" => "F ", "male" => "M ", _ => "" } + range;
        }));

    public static bool TryParsePrizes(string? text, out List<SeriesAwardPrizeDto> prizes, out string? error)
    {
        prizes = [];
        error = null;
        foreach (var line in (text ?? "").Split(['\n', '\r'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sep = line.IndexOfAny([':', '=']);
            if (sep <= 0
                || !int.TryParse(line[..sep].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var place)
                || place < 1 || line[(sep + 1)..].Trim().Length == 0)
            {
                error = $"Не разобрать приз «{line}» — ожидается «место: текст», например «1: 1,000 ₪ + Cup»";
                return false;
            }
            if (prizes.Any(p => p.Place == place))
            {
                error = $"Приз за {place}-е место указан дважды";
                return false;
            }
            prizes.Add(new SeriesAwardPrizeDto(place, line[(sep + 1)..].Trim()));
        }
        return true;
    }

    public static string FormatPrizes(IEnumerable<SeriesAwardPrizeDto> prizes) =>
        string.Join("\n", prizes.OrderBy(p => p.Place).Select(p => $"{p.Place}: {p.Text}"));

    private static bool TryGender(string s, out string? gender)
    {
        gender = s.ToLowerInvariant() switch
        {
            "f" or "ж" or "female" or "w" => "female",
            "m" or "м" or "male" => "male",
            _ => null
        };
        return gender != null;
    }

    private static int? Int(Group g) =>
        g.Success && int.TryParse(g.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
}
