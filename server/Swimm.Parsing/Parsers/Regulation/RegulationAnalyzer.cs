using System.Text;
using System.Text.RegularExpressions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using UglyToad.PdfPig;

namespace Swimm.Parsing.Parsers.Regulation;

/// <summary>
/// Разбор регламента соревнования (תקנון) ради трёх флагов: медали, клубный зачёт,
/// чемпионат Израиля.
///
/// ⚠ Иврит в PDF федерации извлекается ЗАДОМ НАПЕРЁД — так устроены их файлы (тот же
/// эффект ловили в парсере протоколов). Поэтому каждое слово ищем в обоих направлениях, а
/// найденную строку разворачиваем перед показом человеку.
///
/// Ничего не решает сам: возвращает находки с цитатами, галочки ставит админ.
/// </summary>
public class RegulationAnalyzer : IRegulationAnalyzer
{
    /// <summary>
    /// Что ищем в регламенте. Регулярки, а не подстроки: слова идут с артиклями
    /// («הניקוד הקבוצתי», «דרוג הקבוצות») и в разных формах, подстрочный поиск их упускал.
    ///
    /// Формулировки взяты из живых регламентов федерации и Маккаби 2026: клубный зачёт там
    /// называется «командным» (ניקוד קבוצתי), а не «клубным» — искать «דירוג מועדונים»
    /// (как на loglig) бесполезно, в регламентах этого нет.
    /// </summary>
    private static readonly (Regex Rx, string Flag, string Label)[] Markers =
    [
        // Командный (клубный) зачёт.
        (new Regex(@"ה?ניקוד\s+ה?קבוצתי", RegexOptions.Compiled), RegulationFlags.ClubStanding, "ניקוד קבוצתי"),
        (new Regex(@"ד[יר]{1,2}וג\s+ה?קבוצות", RegexOptions.Compiled), RegulationFlags.ClubStanding, "דירוג הקבוצות"),
        (new Regex(@"ה?קבוצות\s+ה?אלופות", RegexOptions.Compiled), RegulationFlags.ClubStanding, "הקבוצות האלופות"),
        (new Regex(@"ד[יר]{1,2}וג\s+ה?(מועדונים|אגודות)", RegexOptions.Compiled), RegulationFlags.ClubStanding, "דירוג אגודות"),
        (new Regex(@"ניקוד\s+ה?(מועדונים|אגודות)", RegexOptions.Compiled), RegulationFlags.ClubStanding, "ניקוד אגודות"),

        // Медали: מדליות / מדלית / מדליה / מדליית. Пробел внутри слова — не опечатка:
        // извлечение PDF рвёт слова («מדליו ת» в регламенте Маккаби-2026).
        (new Regex(@"מדלי\s*(?:ו\s*)?(?:ית|ות|ת|ה)", RegexOptions.Compiled), RegulationFlags.Medals, "מדליות"),

        // Чемпионат Израиля.
        (new Regex(@"אליפות\s+ישראל", RegexOptions.Compiled), RegulationFlags.Championship, "אליפות ישראל"),
    ];

    /// <summary>Больше — уже не помощь, а простыня: админу хватает пары цитат на флаг.</summary>
    private const int MaxQuotesPerFlag = 2;

    public RegulationAnalysisDto Analyze(Stream pdfStream, string fileName)
    {
        string text;
        try
        {
            text = ExtractText(pdfStream);
        }
        catch (Exception ex)
        {
            return new RegulationAnalysisDto(false, false, false, [],
                $"Не удалось прочитать «{fileName}»: {ex.GetType().Name} — {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(text))
            return new RegulationAnalysisDto(false, false, false, [],
                $"В «{fileName}» не нашлось текста — похоже, это скан. Поставьте галочки руками.");

        var findings = Find(text);

        return new RegulationAnalysisDto(
            HasMedals: findings.Any(f => f.Flag == RegulationFlags.Medals),
            HasClubStanding: findings.Any(f => f.Flag == RegulationFlags.ClubStanding),
            IsChampionship: findings.Any(f => f.Flag == RegulationFlags.Championship),
            Findings: findings);
    }

    /// <summary>Чистая функция поиска — тест кормит ею текст, не заводя PDF.</summary>
    public static IReadOnlyList<RegulationFindingDto> Find(string text)
    {
        var findings = new List<RegulationFindingDto>();
        var perFlag = new Dictionary<string, int>();
        var seen = new HashSet<string>();

        foreach (var line in text.Split('\n'))
        {
            var raw = line.Trim();
            if (raw.Length == 0) continue;

            var reversed = Reverse(raw);

            foreach (var (rx, flag, label) in Markers)
            {
                perFlag.TryGetValue(flag, out var count);
                if (count >= MaxQuotesPerFlag) continue;

                // Прямое вхождение — текст лёг нормально; обратное — строка перевёрнута
                // (обычный случай для ивритских PDF федерации).
                var match = rx.Match(raw);
                var source = raw;
                if (!match.Success)
                {
                    match = rx.Match(reversed);
                    source = reversed;
                }
                if (!match.Success) continue;

                var quote = QuoteAround(source, match.Index, match.Length);
                if (!seen.Add($"{flag}|{quote}")) continue;

                findings.Add(new RegulationFindingDto(flag, label, quote));
                perFlag[flag] = count + 1;
            }
        }

        return findings;
    }

    private static string ExtractText(Stream pdfStream)
    {
        using var doc = PdfDocument.Open(pdfStream);
        var sb = new StringBuilder();

        foreach (var page in doc.GetPages())
            sb.AppendLine(page.Text);

        return sb.ToString();
    }

    /// <summary>Разворот строки — ровно то, что нужно перевёрнутому ивриту из PDF.</summary>
    private static string Reverse(string value)
    {
        var chars = value.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    /// <summary>
    /// Кусок текста ВОКРУГ найденного слова.
    ///
    /// ⚠ Почему не «строка целиком»: PdfPig отдаёт страницу одним куском без переносов, и
    /// «строкой» оказывалась вся страница — цитата тогда показывала её начало, а не то место,
    /// по которому мы приняли решение.
    /// </summary>
    private static string QuoteAround(string source, int index, int length)
    {
        var start = Math.Max(0, index - QuoteContext);
        var end = Math.Min(source.Length, index + length + QuoteContext);

        var window = source[start..end];
        var single = Regex.Replace(window, @"\s+", " ").Trim();

        return (start > 0 ? "…" : "") + single + (end < source.Length ? "…" : "");
    }

    /// <summary>Сколько символов показывать по бокам от найденного слова.</summary>
    private const int QuoteContext = 70;
}

/// <summary>Значения <see cref="RegulationFindingDto.Flag"/> — они же ключи в JSON админки.</summary>
public static class RegulationFlags
{
    public const string Medals = "medals";
    public const string ClubStanding = "clubStanding";
    public const string Championship = "championship";
}
