using System.Text.RegularExpressions;

namespace Swimm.Parsing.Parsers.Regulation;

/// <summary>Насколько надёжна находка длины бассейна. Меньше — надёжнее.</summary>
public enum PoolEvidence
{
    /// <summary>Число, «метр» и сразу число дорожек: «25 מ', 8 מסלולים».</summary>
    Lanes = 0,
    /// <summary>Слово «бассейн» вплотную перед числом: «בריכת 25 מ'».</summary>
    PoolOf = 1,
    /// <summary>
    /// «Бассейн» и «N метров» в одной фразе о месте проведения, но не вплотную — так PdfPig
    /// рассыпает слова перевёрнутой строки, либо регламент пишет «בריכה באורך 50 מטר».
    /// </summary>
    Venue = 2,
}

/// <summary>Одна находка длины бассейна в строке регламента.</summary>
/// <param name="Length"><c>25m</c> / <c>50m</c>.</param>
/// <param name="Index">Позиция в сыром тексте строки.</param>
/// <param name="MatchLength">Длина совпадения в сыром тексте.</param>
/// <param name="Reversed">Совпадение в перевёрнутом тексте (слова задом наперёд, цифры прямо).</param>
/// <param name="Rule">Имя сработавшего варианта — для диагностики и тестов.</param>
public sealed record PoolLengthHit(string Length, int Index, int MatchLength, bool Reversed, PoolEvidence Evidence, string Rule);

/// <summary>
/// Длина бассейна из текста регламента (תקנון) — ВСЕ варианты в одном месте (Влад, 25.11.2026:
/// «создай отдельный класс … и там собери все варианты проверки»; И-30 в docs/data-integrity.md).
/// Протокол loglig длину бассейна не печатает, поэтому регламент — единственный источник, а от
/// длины зависят рекорды, очки FINA и личные рекорды. Новый способ записи — новая строка в
/// <see cref="Rules"/> и кейс в <c>PoolLengthFinderTests</c>, а не правка в другом классе.
///
/// Что нужно знать про текст. PdfPig отдаёт ивритскую строку федерации в одном из двух видов:
/// прямом («בריכת מכבי - 25 מ', 8 מסלולים») или перевёрнутом — слова задом наперёд, цифры в
/// нормальном порядке («50 ,רטמ10 .םילולסמ»). Разворачивать всю строку нельзя: перевернутся и
/// цифры (50 → 05), поэтому у каждого варианта есть пара для перевёрнутого текста. В перевёрнутом
/// тексте слова к тому же разъезжаются: «מ'» читается как «'מ» и может уехать от числа, а число —
/// от слова «бассейн».
///
/// Варианты, от надёжного к слабому (<see cref="PoolEvidence"/>):
/// <list type="number">
/// <item><b>Lanes</b> — число, «метр», число дорожек. «25 מ', 8 מסלולים»; перевёрнуто
///   «50 ,רטמ10 .םילולסמ», «50 ,'מ  10  םילולסמ», «25 מ,' 5  םילולסמ» (запятая между буквой и
///   апострофом, loglig doc 3542), «50  ,'מ10 :םיאבה םיכיראתה ןיב ,)םילולסמ» (скобка унесла
///   «дорожки» за даты, takanon 3218 — поэтому до 50 символов).</item>
/// <item><b>PoolOf</b> — «בריכת 25 מ'» / «בבריכת 50 מטר» без дорожек; перевёрнуто «תכירב25»,
///   «תכירבב25» (метр там уезжает, поэтому не требуется). loglig doc 1548.</item>
/// <item><b>Venue</b> — фраза о месте проведения: рядом есть «תתקיים» / «מיקום» / «מקום», слово
///   «бассейн» и «N метров», но не вплотную. Перевёрнуто: «…תכירב ,טייגניו ןוכמב תימואלה הכירבב
///   םייקתת תורחתה50 ב 'מ» (чемпионат на 5 км в бассейне, loglig doc 1541: «בריכת» и «50 מ'»
///   разнесены на сорок символов). Прямо: «בבריכה באורך 50 מטר».</item>
/// </list>
///
/// Чего НЕ считаем длиной бассейна:
/// <list type="bullet">
/// <item>«50 מטר חופשי» — дистанция из программы: без дорожек и без слова «бассейн» не берём;</item>
/// <item>«(בריכת 50 מ')» после названия дистанции — «800 ו-1500 חופשי (בריכת 50 מ')»: это
///   условие допуска (где показано время), а не бассейн старта. Вето — название стиля вплотную
///   перед числом в перевёрнутом тексте и после числа в прямом;</item>
/// <item>«150», «250» — цифра рядом с числом;</item>
/// <item>«בריכה 25» без метра и без дорожек в прямом тексте.</item>
/// </list>
///
/// Слабые находки (<see cref="PoolEvidence.Venue"/>) отбрасываются, если в тексте есть хоть одна
/// надёжная: <see cref="Find"/> отдаёт находки только лучшего уровня. Две разные длины на лучшем
/// уровне — не угадываем (<see cref="Resolve"/> вернёт null), выбирает человек.
/// </summary>
public static class PoolLengthFinder
{
    private const string Len = @"(?<!\d)(?<len>25|50)(?!\d)";

    /// <summary>Сколько символов до числа смотрим в поиске слова «бассейн» и признака места.</summary>
    private const int VenueWindow = 120;

    /// <summary>Сколько символов вокруг числа смотрит вето «это дистанция, а не бассейн».</summary>
    private const int StrokeWindow = 14;

    private sealed record Rule(string Name, Regex Rx, bool Reversed, PoolEvidence Evidence);

    private static readonly Rule[] Rules =
    [
        new("lanes", new(Len + @"\s*(?:מטר|מ['׳])\s*[,\-–]?\s*\d{1,2}[^\n]{0,50}?מסלולים", RegexOptions.Compiled),
            false, PoolEvidence.Lanes),
        new("lanes-reversed", new(Len + @"\s*,?\s*(?:רטמ|['׳]מ|מ\s*,\s*['׳])\s*\d{1,2}[^\n]{0,50}?םילולסמ", RegexOptions.Compiled),
            true, PoolEvidence.Lanes),
        new("pool-of", new(@"בריכת\s*" + Len + @"\s*(?:מטר|מ['׳]?)", RegexOptions.Compiled),
            false, PoolEvidence.PoolOf),
        new("pool-of-reversed", new(@"תכירבב?\s*" + Len, RegexOptions.Compiled),
            true, PoolEvidence.PoolOf),
        new("venue", new(Len + @"\s*(?:מטר|מ['׳])", RegexOptions.Compiled),
            false, PoolEvidence.Venue),
        // Метр может отстоять от числа на обрывок соседнего слова: «50 ב 'מ» = «50 מ' ב[יום]».
        new("venue-reversed", new(Len + @"\s*\S{0,2}\s*(?:['׳]מ|רטמ|מ\s*,?\s*['׳])", RegexOptions.Compiled),
            true, PoolEvidence.Venue),
    ];

    /// <summary>«Бассейн» в любом числе и с предлогом — прямо и перевёрнуто.</summary>
    private static readonly Regex PoolWord = new(@"בריכ[הת]|[הת]כירב", RegexOptions.Compiled);

    /// <summary>Признак фразы о месте проведения: «תתקיים», «מיקום», «מקום» — прямо и перевёрнуто.</summary>
    private static readonly Regex VenueCue = new(@"תתקיים|מיקום|מקום|םייקתת|םוקימ|םוקמ", RegexOptions.Compiled);

    /// <summary>Название стиля ПОСЛЕ числа в прямом тексте: «50 מטר חופשי» — дистанция.</summary>
    private static readonly Regex StrokeAfter =
        new(@"^\s*(?:מטר|מ['׳]?)?\s*(?:חופשי|גב\b|חזה|פרפר|מעורב|חתירה|שליחים)", RegexOptions.Compiled);

    /// <summary>Название стиля вплотную ПЕРЕД числом в перевёрнутом тексте: «ישפוח50».</summary>
    private static readonly Regex StrokeBeforeReversed =
        new(@"(?:ישפוח|הזח|רפרפ|ברועמ|הריתח|םיחילש|(?<![א-ת])בג)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Находки одной строки, все уровни. Одно место строки — одна находка: совпадение, которое
    /// пересекается с уже взятым более надёжным вариантом, пропускается.
    /// </summary>
    public static IReadOnlyList<PoolLengthHit> FindInLine(string raw)
    {
        var hits = new List<PoolLengthHit>();
        foreach (var rule in Rules)
        foreach (Match match in rule.Rx.Matches(raw))
        {
            var number = match.Groups["len"];
            if (rule.Evidence == PoolEvidence.Venue && !IsVenuePhrase(raw, number, rule.Reversed)) continue;
            if (hits.Any(h => match.Index < h.Index + h.MatchLength && h.Index < match.Index + match.Length)) continue;

            hits.Add(new PoolLengthHit(number.Value + "m", match.Index, match.Length, rule.Reversed, rule.Evidence, rule.Name));
        }
        return hits;
    }

    /// <summary>
    /// Находки всего текста — только лучшего уровня, какой нашёлся: есть «с дорожками» или
    /// «בריכת N» — слабые фразы о месте не учитываются вовсе.
    /// </summary>
    public static IReadOnlyList<(string Line, PoolLengthHit Hit)> Find(string text)
    {
        var all = new List<(string Line, PoolLengthHit Hit)>();
        foreach (var line in text.Split('\n'))
        {
            var raw = line.Trim();
            if (raw.Length == 0) continue;
            foreach (var hit in FindInLine(raw)) all.Add((raw, hit));
        }
        if (all.Count == 0) return all;

        var strong = all.Where(x => x.Hit.Evidence != PoolEvidence.Venue).ToList();
        return strong.Count > 0 ? strong : all;
    }

    /// <summary>Длина бассейна по тексту: ровно одна длина среди находок — она; иначе null.</summary>
    public static string? Resolve(string text)
    {
        var lengths = Find(text).Select(x => x.Hit.Length).Distinct().ToList();
        return lengths.Count == 1 ? lengths[0] : null;
    }

    /// <summary>
    /// Фраза о месте проведения: перед числом в пределах <see cref="VenueWindow"/> есть и слово
    /// «бассейн», и признак места, а само число — не дистанция (рядом нет названия стиля).
    /// </summary>
    private static bool IsVenuePhrase(string raw, Group number, bool reversed)
    {
        var from = Math.Max(0, number.Index - VenueWindow);
        var before = raw[from..number.Index];
        if (!PoolWord.IsMatch(before) || !VenueCue.IsMatch(before)) return false;

        if (reversed)
        {
            var near = raw[Math.Max(0, number.Index - StrokeWindow)..number.Index];
            return !StrokeBeforeReversed.IsMatch(near);
        }

        var afterStart = number.Index + number.Length;
        var after = raw[afterStart..Math.Min(raw.Length, afterStart + StrokeWindow + 6)];
        return !StrokeAfter.IsMatch(after);
    }
}
