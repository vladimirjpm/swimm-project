using System;
using System.Collections.Generic;
using System.Linq;

namespace Swimm.Application.Mapping;

/// <summary>Чем английское имя пловца не подходит к ивритскому (см. <see cref="EnglishNameFit"/>).</summary>
public enum EnglishNameVerdict
{
    /// <summary>Подходит: каждое сравнимое латинское слово нашлось среди ивритских.</summary>
    Fits,

    /// <summary>Английского имени нет (или ивритского — сравнивать не с чем).</summary>
    Nothing,

    /// <summary>В латинском поле иврит: двуязычный импорт не нашёл EN-строку и подставил HE.</summary>
    HebrewInLatin,

    /// <summary>Латинское слово не похоже ни на одно ивритское — имя чужое (сдвиг строки в
    /// паре HE+EN, И-47) или само написано иначе.</summary>
    Foreign,
}

/// <summary>
/// Подходит ли английское имя пловца к его ивритскому имени (10.10.2026, после сдвига EN-имён
/// соседей в паре HE+EN — comp 1486, И-47). Сравнение — по скелету согласных
/// (<see cref="NameTransliteration"/>), направление — от ЛАТИНИЦЫ к ивриту: каждое латинское
/// слово обязано найтись среди ивритских. Наоборот нельзя: у loglig часто нет второго имени
/// («פלדמן איתן | צבי» ↔ «FELDMAN EITAN») — лишнее ивритское слово нормально, лишнее
/// латинское — признак чужого имени («פוגל דודוביץ איתן» ↔ «ELKIN DANIEL»).
///
/// Граница имени и фамилии не значит: она бывает неверна и в иврите, и в латинице (И-47),
/// поэтому слова сравниваются общим мешком. Латинское слово сравнивается с каждым ивритским
/// и с каждой парой соседних (склейка «MATANILAN» ↔ «מתן אילן»). Слова, от которых скелет
/// почти пуст («EVA», «HAY», «ABU»), не сравниваются — скелет из одной буквы ничего не решает.
/// </summary>
public static class EnglishNameFit
{
    /// <summary>
    /// Порог похожести слова. Откалиброван 10.10.2026 на 5606 пловцах базы с обоими именами:
    /// см. docs/plans/en-names-handoff.md.
    /// </summary>
    public const double WordThreshold = 0.6;

    /// <summary>
    /// Насколько лучший разрез обязан опережать второй (<see cref="SplitLatinName"/>). Меньше —
    /// имя двусмысленно, решает человек.
    /// </summary>
    private const double SplitMargin = 0.1;

    /// <summary>Меньше двух согласных в скелете — слово не сравнивается.</summary>
    private const int MinSkeleton = 2;

    public static (EnglishNameVerdict Verdict, string? Word, double Score) Assess(
        string? heLast, string? heFirst, string? enLast, string? enFirst)
    {
        var he = $"{heLast} {heFirst}";
        var en = $"{enLast} {enFirst}";
        if (!he.Any(IsHebrew) || string.IsNullOrWhiteSpace(en)) return (EnglishNameVerdict.Nothing, null, 1);
        if (en.Any(IsHebrew)) return (EnglishNameVerdict.HebrewInLatin, null, 0);

        var heWords = Words(he);
        string? worst = null;
        double min = 1;
        int matched = 0, unmatched = 0;
        foreach (var w in Words(en))
        {
            var best = BestSimilarity(w, heWords);
            if (best >= WordThreshold) matched++;
            // Короткое и не совпавшее — не улика: «EVA», «HAY» не с чем сравнить. Совпавшее
            // короткое («ELLA» ↔ «אלה», «GAYA» ↔ «גאיה») засчитывается как обычное.
            else if (NameTransliteration.LatinSkeleton(w).Length < MinSkeleton) continue;
            else unmatched++;
            if (best < min) (min, worst) = (best, w);
        }

        // Одно лишнее латинское слово при двух совпавших — второе имя, которого нет в иврите
        // («MICHEL LEIB | BRUTMAN», «PINTO ASAF | MEIR»): так же решает SplitLatinName. Лишнее
        // при одном совпавшем — уже чужое: близнецы «יבסייב דויד» ↔ «Yavsayev Netanel».
        var foreign = unmatched >= 2 || (unmatched == 1 && matched < 2);
        return foreign
            ? (EnglishNameVerdict.Foreign, worst, min)
            : (EnglishNameVerdict.Fits, null, min);
    }

    /// <summary>
    /// Разрез английского имени одной строкой (карточка loglig: «MICHEL LEIB BRUTMAN»,
    /// а бывает и «SIGRON NOAM» — фамилия первой) на фамилию и имя по ивритским полям.
    ///
    /// Перебираются все разрезы в обоих порядках. Разрез годится, если в каждом поле есть
    /// слово, похожее на слово ТОГО ЖЕ ивритского поля, ни одно слово не похоже только на
    /// другое поле, и не похожее ни на что слово одно и стоит в ИМЕНИ — это второе имя, которого
    /// нет в иврите («MICHEL LEIB | BRUTMAN»). В фамилии такое слово — отказ: так разрез
    /// подгоняется под ивритскую границу, которая сама неверна («עבד | אלקדר» при карточке
    /// «ABED ALRAHMAN ABED ALKADER» дало бы «ABED ALRAHMAN ABED | ALKADER»). Счёт разреза —
    /// сумма похожестей плюс бонус за поле, в котором столько же слов, сколько в ивритском
    /// (правило И-47: «ABU SALEH | ASALA», а не «ABU | SALEH ASALA» — SALEH похоже на оба поля).
    /// Берётся лучший, только если он опережает второй на <see cref="SplitMargin"/>; иначе отказ
    /// (null) — решать человеку.
    /// </summary>
    public static (string Last, string First)? SplitLatinName(string? heLast, string? heFirst, string? latinFull)
    {
        // Латиницу режем только по пробелам: «RON-EL», «LERNER-ALEXANDROV» — одно слово поля,
        // дефис обязан доехать до базы. Похожесть дефис не замечает (NameTransliteration).
        var words = SpaceWords(latinFull ?? "");
        var heL = Words(heLast ?? "");
        var heF = Words(heFirst ?? "");
        if (words.Count < 2 || heL.Count == 0 || heF.Count == 0 || words.Any(w => w.Any(IsHebrew))) return null;
        var (heLCount, heFCount) = (SpaceWords(heLast ?? "").Count, SpaceWords(heFirst ?? "").Count);

        var scored = new List<(double Score, string Last, string First)>();
        for (int k = 1; k < words.Count; k++)
        {
            var head = words.Take(k).ToList();
            var tail = words.Skip(k).ToList();
            foreach (var (last, first) in new[] { (tail, head), (head, tail) })
            {
                if (SplitScore(last, heL, heF, first, heLCount, heFCount) is not double s) continue;
                scored.Add((s, string.Join(' ', last), string.Join(' ', first)));
            }
        }

        if (scored.Count == 0) return null;
        var ordered = scored.OrderByDescending(s => s.Score).ToList();
        if (ordered.Count > 1 && ordered[0].Score - ordered[1].Score < SplitMargin) return null;
        return (ordered[0].Last, ordered[0].First);
    }

    /// <summary>Бонус поля, в котором слов столько же, сколько в ивритском (см. <see cref="SplitLatinName"/>).</summary>
    private const double SameWordCountBonus = 0.3;

    /// <summary>Счёт разреза; null — разрез не годится (см. <see cref="SplitLatinName"/>).</summary>
    private static double? SplitScore(
        List<string> lastEn, List<string> heL, List<string> heF, List<string> firstEn, int heLCount, int heFCount)
    {
        double total = 0;
        int unmatched = 0;
        foreach (var (field, own, other, isLast, ownCount) in new[]
                 { (lastEn, heL, heF, true, heLCount), (firstEn, heF, heL, false, heFCount) })
        {
            bool any = false;
            foreach (var w in field)
            {
                var mine = BestSimilarity(w, own);
                if (mine >= WordThreshold) { any = true; total += mine; }
                // «ABO», «AL», «-»: скелет из одной буквы ничего не решает — слово нейтрально,
                // иначе оно мешает выбрать поле (ABO SALH | REAN).
                else if (NameTransliteration.LatinSkeleton(w).Length < MinSkeleton) continue;
                else if (BestSimilarity(w, other) >= WordThreshold) return null;   // слово другого поля
                else if (isLast) return null;                                      // без пары в фамилии
                else unmatched++;
            }
            if (!any) return null;
            if (field.Count == ownCount) total += SameWordCountBonus;
        }
        return unmatched <= 1 ? total : null;
    }

    private static double BestSimilarity(string latin, List<string> hebrew)
    {
        var cands = hebrew.Concat(hebrew.Zip(hebrew.Skip(1), (a, b) => a + b)).SelectMany(HebrewReadings);
        return cands.SelectMany(c => LatinReadings(latin).Select(l => NameTransliteration.Similarity(c, l)))
            .DefaultIfEmpty(0).Max();
    }

    /// <summary>
    /// Чтения ивритского слова. Скелет <see cref="NameTransliteration"/> выбрасывает «ו» как
    /// гласную, а в именах она часто V: «דוד» ↔ «DAVID», «ויסמן» ↔ «WAIZMAN». Без второго
    /// чтения на калибровке «чужими» выходили ~90 Давидов. Скелет общий с импортом, поэтому
    /// вариант живёт здесь, а не в нём.
    /// </summary>
    private static IEnumerable<string> HebrewReadings(string w)
    {
        yield return w;
        if (w.Contains('ו')) yield return w.Replace('ו', 'ב');
    }

    /// <summary>
    /// Чтения латинского слова:
    /// - «TS» скелет сводит к Z («צ»), а в «ALTSHUL» ↔ «אלטשול» это два звука, «ט» + «ש»
    ///   (пробел скелет выбрасывает, а склейку «TS» ломает);
    /// - «CH» скелет читает как K («כ», «ח»), а в славянских «-VICH», «-TCH» это «ץ» / «צ'»:
    ///   «LEIBOVITCH» ↔ «ליבוביץ», «KITCHEV» ↔ «קיצ׳ב».
    /// </summary>
    private static IEnumerable<string> LatinReadings(string w)
    {
        var u = w.ToUpperInvariant();
        yield return u;
        if (u.Contains("TS")) yield return u.Replace("TS", "T S");
        if (u.Contains("CH")) yield return u.Replace("TCH", "TZ").Replace("CH", "TZ");
    }

    /// <summary>Слова поля как их пишут: только по пробелу (дефис — внутри слова).</summary>
    private static List<string> SpaceWords(string s) =>
        s.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>Слова имени: пробел и дефис делят, гереш («צ'», «ג׳») — часть буквы.</summary>
    private static List<string> Words(string s) =>
        s.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool IsHebrew(char c) => c is >= '֐' and <= '׿';
}
