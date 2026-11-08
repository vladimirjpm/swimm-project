using System;
using System.Collections.Generic;
using System.Linq;
using Swimm.Domain;

namespace Swimm.Application.Mapping;

/// <summary>Личная строка, как она уже лежит у нас (из PDF-импорта).</summary>
/// <param name="Date">Дата дня, «dd/MM/yyyy».</param>
/// <param name="TimeMs">null — без времени (DQ/NS/DNF).</param>
public sealed record PlaceRowInDb(
    long ResultId,
    string Date,
    string StyleName,
    string Distance,
    string Gender,
    int Heat,
    int Lane,
    string FullName,
    int BirthYear,
    int? TimeMs,
    int? Position);

/// <summary>
/// Личная строка пособытийного источника loglig. Пол — из подзаголовка секции («בנות 14»),
/// а у секции без пола («כללי») — из шапки события: страница события у loglig всегда одного пола.
/// </summary>
public sealed record PlaceRowFromSource(
    string Date,
    string StyleName,
    string Distance,
    string Gender,
    string Category,
    int Heat,
    int Lane,
    string FullName,
    int? BirthYear,
    int? TimeMs,
    int? Position,
    /// <summary>Место без времени и без статуса (Р90): место настоящее, время не записали.</summary>
    bool PlacedWithoutTime = false,
    /// <summary>Событие loglig: у чемпионатов своё на каждый возраст, заплывы в каждом с 1 — места внутри него.</summary>
    string Event = "",
    /// <summary>Раунд секции (prelim / final / timed-final): у «мокдамот и финал» места в каждом свои.</summary>
    string Round = "");

/// <summary>Сопоставленная пара: место «до/после» и признаки, по которым её проверили.</summary>
public sealed record PlaceChange(
    long ResultId,
    string Date,
    string StyleName,
    string Distance,
    string Gender,
    string Category,
    string FullName,
    int? PositionBefore,
    int? PositionAfter)
{
    public bool HasChanges => PositionBefore != PositionAfter;

    /// <summary>
    /// Секция «כללי» — единственная, где PDF сводит оба пола в один список: только её места
    /// ремонт пишет. Расхождение в возрастной секции — это то, что напечатано в протоколе
    /// (Р76: двое без времени с 1-м местом, снятый под номером), его решает человек.
    /// </summary>
    public bool IsOpenSection => LogligPlaceMatcher.IsOpen(Category);

    public bool WillApply => HasChanges && IsOpenSection;
}

/// <summary>
/// План ремонта мест. В отличие от эстафет, место каждой личной строки не зависит от
/// соседних, поэтому пишутся все проверенные пары, а расхождения идут отдельным списком.
/// </summary>
public sealed record PlacePlan(
    IReadOnlyList<PlaceChange> Changes,
    IReadOnlyList<string> Mismatches,
    IReadOnlyList<string> Unmatched)
{
    /// <summary>Дубли ключа — единственное, что запрещает запись: пара тогда не доказана.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    public bool CanApply => Problems.Count == 0;
}

/// <summary>
/// Сверка личных строк с пособытийным источником loglig и ремонт мест (И-62, 16891).
///
/// Зачем. PDF-экспорт loglig сводит секцию «גמר ישיר - כללי» девочек и мальчиков одной
/// дисциплины в ОДИН список: пол не печатается, места сквозные. У 16891 в «כללי» попали все
/// 2015 г.р. (своей возрастной секции у них нет), и 379 из 401 строки получили чужое место:
/// ירדן שייכט на 200 на спине — 8-я в общем списке, 3-я у девочек на странице события.
/// Страница события у loglig всегда одного пола, её место и есть протокольное.
///
/// Ключ пары — <b>дата + стиль + дистанция + пол + заплыв + дорожка</b>: в один день одна
/// дорожка одного заплыва дисциплины занята одним пловцом, имена при этом не сравниваются
/// (у источника «’», в базе «׳», вторые имена). Пара считается доказанной, только если
/// совпали и время, и год рождения; иначе место не трогается, а строка идёт в
/// <see cref="PlacePlan.Mismatches"/> — это «перепроверь всё»: расхождение времени тоже
/// находка, но чинит его человек, а не этот прогон.
///
/// Чистая функция: на входе две плоские выборки, на выходе план. Ни сети, ни БД.
/// </summary>
public static partial class LogligPlaceMatcher
{
    public static PlacePlan Build(
        IReadOnlyList<PlaceRowFromSource> source,
        IReadOnlyList<PlaceRowInDb> db)
    {
        // Сведение по ключу; общий ключ (чемпионаты, раунды) — по времени и году (Pairing.cs).
        var (pairs, unmatched, problems) = PairRows(source, db);

        var placeOf = SectionPlaces(source);
        var mismatches = new List<string>();
        var changes = new List<PlaceChange>();
        foreach (var (src, ours, key) in pairs)
        {
            var sameTime = src.TimeMs == ours.TimeMs;
            var sameYear = SameYear(src.BirthYear, ours.BirthYear);
            if (!sameTime || !sameYear)
            {
                mismatches.Add($"{key} · {ours.FullName}: " +
                               (sameTime ? "" : $"время у нас {Ms(ours.TimeMs)}, у источника {Ms(src.TimeMs)}; ") +
                               (sameYear ? "" : $"год у нас {ours.BirthYear}, у источника {src.BirthYear}; ") +
                               "место не трогаем");
                continue;
            }

            // Без времени места нет (И-18). Место — из секции источника, но без снятых: см. SectionPlaces.
            int? position = placeOf.TryGetValue(src, out var place) ? place : null;
            changes.Add(new PlaceChange(
                ours.ResultId, ours.Date, ours.StyleName, ours.Distance, ours.Gender, src.Category,
                ours.FullName, ours.Position, position));
        }

        return new PlacePlan(
            changes.OrderBy(c => c.Date, StringComparer.Ordinal)
                .ThenBy(c => c.StyleName, StringComparer.Ordinal)
                .ThenBy(c => c.Distance, StringComparer.Ordinal)
                .ThenBy(c => c.Gender, StringComparer.Ordinal)
                .ThenBy(c => c.PositionAfter ?? int.MaxValue)
                .ToList(),
            mismatches,
            unmatched)
        { Problems = problems };
    }

    /// <summary>
    /// Место строки внутри её секции (день + дисциплина + пол + категория) — по номерам
    /// источника, но только среди строк СО временем: 1 + сколько пловцов со временем стоят
    /// строго выше. Страница события нумерует подряд и снятых (200 комплексом, мальчики 14 у
    /// 16891: снятый — 11-й, следующие — 12 и 13), а PDF и наше правило И-18 мест им не дают:
    /// там те же двое — 11 и 12. Ничья (один номер у двоих) сохраняется.
    ///
    /// Секция «כללי» бывает двух видов, и место в ней считается по-разному (решения Влада 07.11.2026):
    /// <list type="bullet">
    /// <item><b>Остаток без своей секции</b> — в «כללי» есть пловцы, чей возраст попал в пропуск
    /// лестницы возрастных секций, то есть своя секция им положена, но её нет (16891: секции 11 и 13,
    /// а 12-летние 2015 г.р. — в «כללי» вместе с 18-летними). Организатор награждает по возрасту —
    /// место внутри ГОДА РОЖДЕНИЯ: דניאל קיננסברונר — 4 золота, а не 3 и серебро (на 50
    /// баттерфляем выше него стоял 18-летний מטביי קוצירנקו), אביעד לוקה — три серебра.</item>
    /// <item><b>Общий зачёт старших</b> — в «כללי» только те, кто старше всех возрастных секций
    /// (лиги: секции 9–13, а в «כללי» 14–18 лет). Место — как на странице loglig, внутри пола,
    /// без деления по году: иначе на одну дисциплину выходило по четыре «первых».</item>
    /// </list>
    /// </summary>
    private static Dictionary<PlaceRowFromSource, int> SectionPlaces(IReadOnlyList<PlaceRowFromSource> source)
    {
        var splitByYear = OpenSectionsSplitByYear(source);
        var places = new Dictionary<PlaceRowFromSource, int>(ReferenceEqualityComparer.Instance);
        foreach (var section in source.GroupBy(r =>
                     $"{EventKey(r)}|{r.Round}|{r.Category}" +
                     (IsOpen(r.Category) && splitByYear.Contains(EventKey(r)) ? $"|{r.BirthYear}" : "")))
        {
            // «Место без времени» (Р90) — настоящее место: время не записали, а пловец проплыл.
            var timed = section.Where(r => (r.TimeMs is not null || r.PlacedWithoutTime) && r.Position is not null).ToList();
            foreach (var row in timed)
                places[row] = 1 + timed.Count(other => other.Position < row.Position);
        }
        return places;
    }

    /// <summary>Событие: день + дисциплина + пол + событие loglig (у чемпионатов — своё на каждый возраст).</summary>
    private static string EventKey(PlaceRowFromSource r) => $"{r.Date}|{r.StyleName}|{r.Distance}|{r.Gender}|{r.Event}";

    /// <summary>
    /// События (день + дисциплина + пол), где «כללי» — остаток без своей секции: в нём есть пловец,
    /// чей возраст лежит ВНУТРИ лестницы возрастных секций события, но своей секции у него нет
    /// (16891: секции 11 и 13, а 12-летний — в «כללי»). Возраст — сезонный (год окончания сезона
    /// минус год рождения), как печатает организатор в «בנות 13».
    ///
    /// Пловец МЛАДШЕ самой младшей секции — не остаток: он плывёт «вверх», в открытом зачёте.
    /// Так на национальных чемпионатах (273, лето 2025: секции 15, 16, 17-18, а 14-летний — в
    /// «כללי» вместе со взрослыми); там место — общее, как на странице loglig (решение Влада 08.11.2026).
    /// </summary>
    private static HashSet<string> OpenSectionsSplitByYear(IReadOnlyList<PlaceRowFromSource> source)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ev in source.GroupBy(EventKey))
        {
            var covered = ev.Where(r => !IsOpen(r.Category))
                .Select(r => SectionAges(r.Category)).OfType<(int From, int To)>().Distinct().ToList();
            if (covered.Count == 0) continue;
            var youngest = covered.Min(s => s.From);
            var oldest = covered.Max(s => s.To);
            var seasonEnd = SeasonEndYear(ev.First().Date);
            if (seasonEnd is null) continue;

            bool InGap(int age) => age > youngest && age < oldest && !covered.Any(s => age >= s.From && age <= s.To);
            if (ev.Any(r => IsOpen(r.Category) && r.BirthYear is > 0 && InGap(seasonEnd.Value - r.BirthYear.Value)))
                result.Add(ev.Key);
        }
        return result;
    }

    /// <summary>«בנות 13» → (13, 13); «גברים 17-18» → (17, 18); «כללי» — null.</summary>
    private static (int From, int To)? SectionAges(string category)
    {
        var tokens = category.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 2) return null;
        if (int.TryParse(tokens[1], out var age)) return (age, age);
        var range = tokens[1].Split('-');
        return range.Length == 2 && int.TryParse(range[0], out var from) && int.TryParse(range[1], out var to) && from <= to
            ? (from, to)
            : null;
    }

    /// <summary>«06/11/2026» → 2027: год окончания сезона (единое правило — <see cref="SeasonMath"/>).</summary>
    private static int? SeasonEndYear(string date) =>
        DateTime.TryParseExact(date, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d)
            ? SeasonMath.StartYearOf(d) + 1
            : null;

    /// <summary>Секция «כללי» — общая, без возрастной категории.</summary>
    public static bool IsOpen(string category) => category.Contains("כללי", StringComparison.Ordinal);

    /// <summary>
    /// Пол секции: «בנות 14» / «נשים 13-99» — female, «בנים» / «גברים» — male. Секция без
    /// пола («כללי») берёт пол шапки события.
    /// </summary>
    public static string SectionGender(string category, string eventGender)
    {
        var first = category.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        return first switch
        {
            "בנות" or "נשים" or "נערות" => "female",
            "בנים" or "גברים" or "נערים" => "male",
            _ => eventGender
        };
    }

    private static string Key(string date, string style, string distance, string gender, int heat, int lane) =>
        $"{date}|{style.ToLowerInvariant()}|{distance.ToLowerInvariant()}|{gender}|h{heat}|l{lane}";

    private static string Ms(int? ms) => ms is int v ? $"{v / 60000:00}:{v / 1000 % 60:00}.{v / 10 % 100:00}" : "без времени";
}
