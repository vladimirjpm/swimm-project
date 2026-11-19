using System.Text;

namespace Swimm.Application.Mapping;

/// <summary>
/// Ключ поиска места проведения в справочнике бассейнов (П4, <c>Sys_VenuePools</c>).
///
/// isr.org.il пишет одно и то же место по-разному: «בריכת ויצ"ו נהלל» / «בריכת ויצו נהלל»,
/// «בריכת וייסגל» / «בריכת וויסגל», «מרכז הספורט תלפ"ז- ארמון הנציב» / «… תלפ"ז ארמון הנציב»,
/// «בריכת מכבי חיפה» / «מכבי חיפה». Нормализация снимает только ОРФОГРАФИЮ:
/// <list type="bullet">
/// <item>огласовки и знаки кантилляции, кавычки и гереш (״ ׳ " ' ” “ ’ ‘ `) — убираются;</item>
/// <item>прочая пунктуация (дефис, макаф, запятая, точка, скобки, слэш) — становится пробелом;</item>
/// <item>удвоенные «וו» и «יי» (полное/неполное письмо) — одной буквой;</item>
/// <item>служебное слово «бассейн» в начале (בריכת / בריכה / הבריכה / בריכות) — отбрасывается;</item>
/// <item>латиница — в нижний регистр, пробелы схлопываются.</item>
/// </list>
/// Чего она НЕ делает сознательно: не выкидывает город («בריכת וייסגל רחובות» ≠ «בריכת וייסגל»)
/// и не угадывает опечатки («חוות הנוער בציוני») — это синонимы, их подтверждает человек на
/// странице /Admin/VenuePools. Склеить нормализацией два разных места хуже, чем не узнать одно.
/// </summary>
public static class VenueNameNormalizer
{
    private static readonly string[] LeadingPoolWords = ["בריכת", "בריכה", "הבריכה", "בריכות"];

    /// <summary>Нормализованная форма; null — названия нет (пусто, только пунктуация или одно «בריכה»).</summary>
    public static string? Normalize(string? venue)
    {
        if (string.IsNullOrWhiteSpace(venue)) return null;

        var sb = new StringBuilder(venue.Length);
        foreach (var ch in venue.Normalize(NormalizationForm.FormC))
        {
            if (IsDropped(ch)) continue;
            if (IsSeparator(ch)) { sb.Append(' '); continue; }
            sb.Append(char.ToLowerInvariant(ch));
        }

        var tokens = sb.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CollapseDoubledLetters)
            .ToList();

        while (tokens.Count > 0 && LeadingPoolWords.Contains(tokens[0]))
            tokens.RemoveAt(0);

        return tokens.Count == 0 ? null : string.Join(' ', tokens);
    }

    /// <summary>Огласовки/кантилляция (U+0591–U+05C7, кроме макафа U+05BE), кавычки и гереш.</summary>
    private static bool IsDropped(char ch) =>
        (ch >= '֑' && ch <= 'ׇ' && ch != '־')
        || ch is '"' or '\'' or '`' or '׳' or '״' or '“' or '”' or '„'
            or '‘' or '’' or '´';

    private static bool IsSeparator(char ch) =>
        char.IsWhiteSpace(ch)
        || ch is '־' or '-' or '‐' or '‑' or '‒' or '–' or '—'
            or ',' or '.' or ';' or ':' or '(' or ')' or '[' or ']' or '/' or '\\' or '|' or '_';

    private static string CollapseDoubledLetters(string token)
    {
        if (token.Length < 2) return token;
        var sb = new StringBuilder(token.Length);
        foreach (var ch in token)
        {
            if (sb.Length > 0 && (ch == 'ו' || ch == 'י') && sb[^1] == ch) continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
