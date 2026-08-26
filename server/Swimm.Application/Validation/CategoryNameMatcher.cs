using System.Text.RegularExpressions;

namespace Swimm.Application.Validation;

/// <summary>Категория глазами подборщика: ключ, слова и возрастная полоса.</summary>
/// <param name="Key">Ключ категории (<c>results-youth-team</c> и т.п.).</param>
/// <param name="Words">Название и его ивритский вариант — как они заведены в /Admin/Categories.</param>
/// <param name="MinAge">Низ возрастной полосы; null — категория не про возраст.</param>
/// <param name="MaxAge">Верх полосы; null — открыта сверху (Adults 17+) либо не про возраст.</param>
public sealed record CategoryWord(
    string Key, IReadOnlyCollection<string> Words, int? MinAge = null, int? MaxAge = null);

/// <summary>
/// Подбор категорий соревнования ПО ЕГО НАЗВАНИЮ (правило Влада 2026-08-23): «есть в названии
/// слово из /Admin/Categories — применяй; если там не только эти возраста — добавляй 8-99».
///
/// Слова берутся из самой таблицы категорий (Name + NameHe), а не зашиты сюда: завёл новую
/// категорию — она сразу участвует в подборе, и переименование не расходится с кодом.
///
/// Чистая функция: ни БД, ни сети.
/// </summary>
public static class CategoryNameMatcher
{
    /// <summary>Универсальная витрина «все возрасты» — она же запасной вариант.</summary>
    public const string AllAgesKey = "results-8-99";

    /// <summary>
    /// Возрастной диапазон цифрами: «לגילאי 8-11», «גילאי 11-10», «13-14». Годы пишут в обе
    /// стороны («11-10»), поэтому границы потом сортируются.
    ///
    /// Двузначные числа и только они: год («2026») и дистанция («1500») под шаблон не попадают.
    /// </summary>
    private static readonly Regex AgeRangeRx = new(
        @"(?<!\d)(\d{1,2})\s*[-–]\s*(\d{1,2})(?!\d)",
        RegexOptions.Compiled);

    /// <summary>
    /// Возраст в названии, но одним числом с плюсом: «17+», «גילאי 25+».
    /// </summary>
    private static readonly Regex AgeOpenRx = new(@"(?<!\d)(\d{1,2})\s*\+", RegexOptions.Compiled);

    /// <summary>
    /// Сколько лет должны совпасть, чтобы диапазон отнесли к полосе. Полосы СМЫКАЮТСЯ
    /// (Kids 8–11, Young 11–14): при пороге в один год «8-11» цеплял бы ещё и Young за
    /// общую одиннадцатилетку, чего Влад руками никогда не делал.
    /// </summary>
    private const int MinOverlapYears = 2;

    /// <summary>
    /// Что предложить отметить в превью.
    /// </summary>
    /// <param name="competitionName">Название соревнования с сайта.</param>
    /// <param name="categories">Категории из БД со словами (Name, NameHe).</param>
    /// <param name="isMasters">В файле есть мастерс-заплывы — тогда Masters ставится и без слова.</param>
    /// <returns>Ключи категорий; порядок не важен, дубликатов нет.</returns>
    public static IReadOnlyList<string> Suggest(
        string? competitionName,
        IReadOnlyList<CategoryWord> categories,
        bool isMasters = false)
    {
        var name = (competitionName ?? "").Trim();
        var found = new List<string>();

        foreach (var category in categories)
        {
            if (category.Key == AllAgesKey) continue;   // добавляется по правилу ниже, не по слову
            if (category.Words.Any(w => ContainsWord(name, w)))
                found.Add(category.Key);
        }

        // Мастерс узнаётся ещё и по содержимому файла: в названии его пишут не всегда
        // («ליגת ותיקים»), а признак у заплывов проставлен парсером.
        if (isMasters && categories.Any(c => c.Key == "results-masters") && !found.Contains("results-masters"))
            found.Add("results-masters");

        // Возраст, названный ЦИФРАМИ («לגילאי 8-11»), — такое же указание, как слово: Влад
        // таким соревнованиям ставил Kids, а не «все возраста». Полосы берём из самой таблицы
        // категорий (колонки MinAge/MaxAge), поэтому лестницу правят в админке, а не в коде.
        var fromNumbers = MatchBands(name, categories);
        var numbersFound = fromNumbers.Count > 0;
        foreach (var key in fromNumbers)
            if (!found.Contains(key)) found.Add(key);

        // «Не только эти возраста» → универсальная витрина. Это либо старт, про возраст
        // которого в названии не сказано ничего, либо диапазон, не легший ни в одну полосу
        // («8-99», «10-60»): значит он шире лестницы.
        var ageWordFound = found.Any(k => categories.Any(c => c.Key == k && c.MinAge != null));
        if (!ageWordFound && !numbersFound)
            found.Add(AllAgesKey);
        else if (HasAgeNumbers(name) && !numbersFound)
            found.Add(AllAgesKey);

        return found.Distinct().ToList();
    }

    /// <summary>Есть ли в названии возраст, названный цифрами.</summary>
    private static bool HasAgeNumbers(string name) =>
        AgeRangeRx.IsMatch(name) || AgeOpenRx.IsMatch(name);

    /// <summary>
    /// Полосы, в которые попадает возраст из названия. Диапазон относится к полосе, если они
    /// перекрываются минимум на <see cref="MinOverlapYears"/> года — смежные полосы делят
    /// границу, и одного общего года мало.
    /// </summary>
    private static List<string> MatchBands(string name, IReadOnlyList<CategoryWord> categories)
    {
        var bands = categories.Where(c => c.MinAge is not null).ToList();
        if (bands.Count == 0) return [];

        var ranges = new List<(int From, int To)>();

        foreach (Match m in AgeRangeRx.Matches(name))
        {
            var a = int.Parse(m.Groups[1].Value);
            var b = int.Parse(m.Groups[2].Value);
            // «11-10» и «10-11» — одно и то же: сортируем.
            ranges.Add((Math.Min(a, b), Math.Max(a, b)));
        }

        foreach (Match m in AgeOpenRx.Matches(name))
            ranges.Add((int.Parse(m.Groups[1].Value), OpenTopAge));

        var keys = new List<string>();
        foreach (var (from, to) in ranges)
        {
            // Диапазон, который перекрывает всю лестницу, — это не «Kids и Young и Juniors»,
            // а «все возраста»: такие («8-99») отдаём общему правилу, а не полосам.
            if (bands.All(b => Overlap(from, to, b) >= MinOverlapYears)) continue;

            foreach (var band in bands)
                if (Overlap(from, to, band) >= MinOverlapYears && !keys.Contains(band.Key))
                    keys.Add(band.Key);
        }

        return keys;
    }

    /// <summary>Верх открытой полосы («17+») — с запасом, мастерс плавает и в 90.</summary>
    private const int OpenTopAge = 99;

    /// <summary>Сколько лет общего у диапазона из названия и у полосы категории.</summary>
    private static int Overlap(int from, int to, CategoryWord band)
    {
        var bandFrom = band.MinAge!.Value;
        var bandTo = band.MaxAge ?? OpenTopAge;
        return Math.Min(to, bandTo) - Math.Max(from, bandFrom) + 1;
    }

    /// <summary>
    /// Слово в названии — с границами, а не подстрокой. У иврита нет регистра и словоформ,
    /// которые нас волнуют, но есть приставки-предлоги («לצעירים», «וبוגרים»), поэтому
    /// достаточно, чтобы слово входило как отдельный кусок текста без букв вплотную справа.
    /// </summary>
    private static bool ContainsWord(string name, string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return false;

        var idx = name.IndexOf(word.Trim(), StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            var after = idx + word.Trim().Length;
            var tailOk = after >= name.Length || !char.IsLetter(name[after]);
            if (tailOk) return true;
            idx = name.IndexOf(word.Trim(), idx + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
