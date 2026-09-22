using System.Text;
using System.Text.RegularExpressions;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using UglyToad.PdfPig;

namespace Swimm.Parsing.Parsers.Regulation;

/// <summary>
/// Разбор регламента соревнования (תקנון) ради трёх флагов: медали, клубный зачёт,
/// чемпионат Израиля — и длины бассейна (И-30: протокол её не пишет, и летний чемпионат лёг 25 м).
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

        // Чемпионат Израиля. Осторожно: регламент обычного турнира сплошь и рядом упоминает
        // чемпионат как ЦЕЛЬ подготовки — см. Veto ниже.
        (new Regex(@"אליפות\s+ישראל", RegexOptions.Compiled), RegulationFlags.Championship, "אליפות ישראל"),
    ];

    /// <summary>
    /// Когда упоминание чемпионата НЕ делает соревнование чемпионатом.
    ///
    /// Живой случай — «מילניום 2025» (compID 16739): единственное вхождение «אליפות ישראל»
    /// во всём регламенте стоит во вступительном слове — «תחרות המילניום … תהווה להתחדד
    /// ולהתכונן באופן מיטבי לאליפות ישראל», то есть «станет возможностью подготовиться К
    /// чемпионату Израиля». Регламент говорит прямо обратное тому, что мы из него читали, а
    /// галочка «Чемпионат Израиля» уходила в БД и меняла вид клубного зачёта
    /// (StandingKinds.Resolve).
    ///
    /// Одного предлога ל־ мало: «התקנון לאליפות ישראל» — это уже настоящий чемпионат.
    /// Отличают именно слова ПОДГОТОВКИ рядом, и это устойчивая формула федерации для
    /// разминочных стартов перед чемпионатом, а не особенность «Миллениума».
    ///
    /// Вторая формула — ССЫЛКА на чемпионат как на образец: «ליגה מס 1 צעירים» (compID 16752)
    /// пишет «קבוצות הגיל בליגה זהות לקבוצות הגיל באליפות ישראל לצעירים» — «возрастные группы
    /// в лиге такие же, как на чемпионате Израиля». Тоже не чемпионат.
    /// </summary>
    private static readonly Regex ChampionshipVeto =
        new(@"להתכונן|להתחדד|לקראת|הכנה|זהות|זהה|בהתאם", RegexOptions.Compiled);

    /// <summary>
    /// Длина бассейна: число, «метр» и СРАЗУ число дорожек — «בריכת מכבי - 25 מ', 8 מסלולים».
    /// Дорожки обязательны: без них «50 מטר» — это дистанция заплыва из программы, а не бассейн.
    /// Перевёрнутую строку PDF федерации ловит второй вариант прямо в сыром тексте: цифры
    /// PdfPig там отдаёт в нормальном порядке, а слова — задом наперёд («50 ,רטמ10 .םילולסמ»);
    /// разворот всей строки перевернул бы сами цифры (50 → 05). «מ'» там читается как «'מ»
    /// («50 ,'מ  10  םילולסמ» — лига длинных бассейнов, takanon 14575). Слово «дорожки» может
    /// уехать от числа: скобка ломает порядок, «50  ,'מ10 :םיאבה םיכיראתה ןיב ,)םילולסמ»
    /// (летние отборы 2026, takanon 3218) — поэтому до 50 символов, а не вплотную.
    /// </summary>
    private static readonly Regex[] PoolRx =
    [
        new(@"(?<!\d)(?<len>25|50)\s*(?:מטר|מ['׳])\s*[,\-–]?\s*\d{1,2}[^\n]{0,50}?מסלולים", RegexOptions.Compiled),
        new(@"(?<!\d)(?<len>25|50)\s*,?\s*(?:רטמ|['׳]מ)\s*\d{1,2}[^\n]{0,50}?םילולסמ", RegexOptions.Compiled),
    ];

    /// <summary>
    /// Сколько символов вокруг находки смотрит вето. Не вся строка: PdfPig отдаёт страницу
    /// одним куском, и по всей странице «подготовка» нашлась бы почти в любом регламенте.
    /// </summary>
    private const int VetoContext = 120;

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
            Findings: findings,
            PoolType: PoolTypeOf(findings));
    }

    /// <summary>Бассейн по находкам: ровно одна длина — она, обе или ни одной — null.</summary>
    public static string? PoolTypeOf(IReadOnlyList<RegulationFindingDto> findings)
    {
        var lengths = findings.Where(f => f.Flag == RegulationFlags.Pool)
            .Select(f => f.Matched).Distinct().ToList();
        return lengths.Count == 1 ? lengths[0] : null;
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

            foreach (var rx in PoolRx)
            foreach (Match match in rx.Matches(raw))
            {
                var len = match.Groups["len"].Value + "m";
                // Цитата — из развёрнутой строки, чтобы админ прочитал её по-человечески; у
                // прямого текста — как есть.
                var quote = rx == PoolRx[0]
                    ? QuoteAround(raw, match.Index, match.Length)
                    : DigitsForward(QuoteAround(reversed, raw.Length - match.Index - match.Length, match.Length));
                if (seen.Add($"{RegulationFlags.Pool}|{len}|{quote}"))
                    findings.Add(new RegulationFindingDto(RegulationFlags.Pool, len, quote));
            }

            foreach (var (rx, flag, label) in Markers)
            {
                perFlag.TryGetValue(flag, out var count);
                if (count >= MaxQuotesPerFlag) continue;

                // Прямое вхождение — текст лёг нормально; обратное — строка перевёрнута
                // (обычный случай для ивритских PDF федерации).
                var source = rx.IsMatch(raw) ? raw : reversed;

                // Все вхождения, а не первое: одно может быть отклонено вето (подготовка к
                // чемпионату), и оно не должно прятать настоящее упоминание дальше по тексту —
                // тем более что PdfPig отдаёт страницу ОДНОЙ строкой.
                foreach (Match match in rx.Matches(source))
                {
                    if (perFlag.GetValueOrDefault(flag) >= MaxQuotesPerFlag) break;
                    if (flag == RegulationFlags.Championship && VetoedNearby(source, match)) continue;

                    var quote = QuoteAround(source, match.Index, match.Length);
                    if (!seen.Add($"{flag}|{quote}")) continue;

                    findings.Add(new RegulationFindingDto(flag, label, quote));
                    perFlag[flag] = perFlag.GetValueOrDefault(flag) + 1;
                }
            }
        }

        return findings;
    }

    /// <summary>Стоят ли рядом с находкой слова подготовки — см. <see cref="ChampionshipVeto"/>.</summary>
    private static bool VetoedNearby(string source, Match match)
    {
        var start = Math.Max(0, match.Index - VetoContext);
        var end = Math.Min(source.Length, match.Index + match.Length + VetoContext);
        return ChampionshipVeto.IsMatch(source[start..end]);
    }

    private static string ExtractText(Stream pdfStream)
    {
        using var doc = PdfDocument.Open(pdfStream);
        var sb = new StringBuilder();

        foreach (var page in doc.GetPages())
            sb.AppendLine(page.Text);

        return sb.ToString();
    }

    /// <summary>
    /// Цифры в перевёрнутом PDF и так шли в нормальном порядке, и разворот строки их испортил
    /// («50» → «05») — возвращаем каждую группу цифр обратно. Только для цитаты человеку.
    /// </summary>
    private static string DigitsForward(string value) =>
        Regex.Replace(value, @"\d+", m => Reverse(m.Value));

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
    public const string Pool = "pool";
}
