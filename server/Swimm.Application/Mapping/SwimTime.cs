using System.Text.RegularExpressions;

namespace Swimm.Application.Mapping;

/// <summary>
/// Парс/формат времени заплыва «строка ↔ миллисекунды». Логика парса совпадает с
/// импортом (JsonImportService.ParseTimeToMs): формат <c>[[h:]m:]ss.ff[f]</c>, запятая = точка.
/// Используется формой ручной правки результата (Admin/Results).
/// </summary>
public static partial class SwimTime
{
    [GeneratedRegex(@"^(?:(?:(\d+):)?(\d+):)?(\d+)\.(\d+)$")]
    private static partial Regex TimeRx();

    /// <summary>«1:02.34» / «58.21» / «00:40:29.16» (длинные дистанции) → мс.
    /// null — пусто или не распознано.</summary>
    public static int? ParseToMs(string? time)
    {
        if (string.IsNullOrWhiteSpace(time)) return null;

        time = time.Trim().Replace(',', '.');
        var match = TimeRx().Match(time);
        if (!match.Success) return null;

        int hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        int minutes = (match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0) + hours * 60;
        int seconds = int.Parse(match.Groups[3].Value);
        string frac = match.Groups[4].Value;

        frac = frac.Length switch
        {
            1 => frac + "00",
            2 => frac + "0",
            3 => frac,
            _ => frac[..3]
        };
        int ms = int.Parse(frac);

        return (minutes * 60 + seconds) * 1000 + ms;
    }

    /// <summary>мс → «1:02.34» / «58.21». null → пустая строка. Сотые, если хватает точности.</summary>
    public static string FormatMs(int? totalMs)
    {
        if (totalMs is not int ms || ms < 0) return string.Empty;

        int minutes = ms / 60000;
        int seconds = ms / 1000 % 60;
        int frac = ms % 1000;
        // Времена плавания — сотые: если тысячные кратны 10, показываем 2 знака, иначе 3.
        string fracStr = frac % 10 == 0 ? $"{frac / 10:D2}" : $"{frac:D3}";

        return minutes > 0
            ? $"{minutes}:{seconds:D2}.{fracStr}"
            : $"{seconds}.{fracStr}";
    }
}
