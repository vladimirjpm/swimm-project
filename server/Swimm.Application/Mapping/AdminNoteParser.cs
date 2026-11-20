using System.Globalization;
using System.Text.RegularExpressions;

namespace Swimm.Application.Mapping;

/// <summary>Разобранная справка: заголовок, дата «Проверено», тело без этих двух строк.</summary>
public sealed record ParsedAdminNote(string Title, DateOnly? Checked, string Body);

/// <summary>
/// Формат справки админки (docs/admin-notes/README.md): первая непустая строка — <c># Заголовок</c>,
/// следующая непустая — «Проверено: ДД.ММ.ГГГГ», дальше Markdown. Отступления не роняют
/// справку: нет заголовка — заголовком служит ключ, нет даты — <c>Checked = null</c>, и всё
/// остальное идёт в тело как есть.
/// </summary>
public static partial class AdminNoteParser
{
    /// <summary>Ключ справки: латиница в нижнем регистре, цифры, точка и дефис; без слэшей и «..».</summary>
    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]|\.(?!\.))*$")]
    private static partial Regex KeyShape();

    [GeneratedRegex(@"^\s*\**\s*Проверено\s*\**\s*:?\s*\**\s*(\d{1,2}\.\d{1,2}\.\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex CheckedLine();

    public const int MaxKeyLength = 100;

    /// <summary>Годится ли строка в ключ (форма имени файла). Существование файла проверяет хранилище.</summary>
    public static bool IsValidKey(string? key) =>
        !string.IsNullOrEmpty(key) && key.Length <= MaxKeyLength && !key.EndsWith('.') && KeyShape().IsMatch(key);

    public static ParsedAdminNote Parse(string markdown, string fallbackTitle)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        string? title = null;
        DateOnly? checkedOn = null;

        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;
        if (i < lines.Length && lines[i].StartsWith("# "))
        {
            title = lines[i][2..].Trim();
            i++;
        }

        var j = i;
        while (j < lines.Length && string.IsNullOrWhiteSpace(lines[j])) j++;
        if (j < lines.Length && CheckedLine().Match(lines[j]) is { Success: true } m)
        {
            if (DateOnly.TryParseExact(m.Groups[1].Value, ["d.M.yyyy", "dd.MM.yyyy"], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var d))
                checkedOn = d;
            i = j + 1;
        }

        var body = string.Join('\n', lines[i..]).Trim('\n');
        return new ParsedAdminNote(string.IsNullOrWhiteSpace(title) ? fallbackTitle : title, checkedOn, body);
    }
}
