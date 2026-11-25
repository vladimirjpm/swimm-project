using System.Globalization;
using System.Text;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Ключ заплыва для сверки «новый день против лежащих»: человек (имя без пробелов и знаков + год),
/// дисциплина (стиль + дистанция), заплыв и дорожка. Совпадение у многих строк значит, что
/// «другой день» — на деле тот же протокол (Р124).
/// </summary>
/// <summary>Личная строка для сверки: ключи обоих способов и день (у строк базы).</summary>
public sealed record SiblingSwim(string Person, int BirthYear, string Style, string Distance, int Heat, int Lane, int? TimeMs,
    DateOnly? Day = null);

/// <summary>Каким ключом сверяли: заплыв+дорожка, время, нечем.</summary>
public enum OverlapBy { HeatLane, Time, None }

/// <summary>Итог сверки: совпавшие строки файла, личных строк в файле и день, где лежат совпавшие (если один).</summary>
public sealed record OverlapResult(OverlapBy By, IReadOnlyList<SiblingSwim> Matched, int FileRows, DateOnly? InDay);

public sealed record SiblingSwimKey(string Person, int BirthYear, string Style, string Distance, int Heat, int Lane);

/// <summary>
/// Р124: «Затянуть этот день» — день старта лежит на loglig отдельным соревнованием-соседом
/// (compID 488 → 9817, 6611 → 11590). Чистые правила: годится ли кандидат, годится ли дата
/// протокола, не тот же ли это протокол, бассейн по умолчанию. Тесты — <c>SiblingDayRulesTests</c>.
/// Одни и те же правила зовут превью и запись (сервер перепроверяет всё при «Импортировать»).
/// </summary>
public static class SiblingDayRules
{
    /// <summary>Совпавших заплывов, начиная с которых новый день считается тем же протоколом.</summary>
    public const int OverlapMinRows = 3;

    /// <summary>…и не меньше этой доли личных строк файла (одно-два случайных совпадения — не повод).</summary>
    public const double OverlapShare = 0.05;

    /// <summary>
    /// Годится ли сосед <paramref name="logligId"/> для кнопки: номер в ±<see cref="ProgrammeCheckRules.NeighbourRadius"/>
    /// от привязанного, то же название без дня недели и дат (<see cref="LogligTitleRules.Normalize"/>),
    /// ровно одна дата — и она в недостающем дне федерации. null — годится; иначе причина отказа.
    /// </summary>
    public static string? CandidateRefusal(int? boundLogligId, string? boundTitle, int logligId,
        LogligCompetitionSummaryDto? candidate, DateOnly siteStart, DateOnly siteEnd, IReadOnlyList<DateOnly> dbDays)
    {
        if (boundLogligId is not int bound)
            return "у федерации не указана ссылка на результаты — сравнивать не с чем";
        if (logligId == bound)
            return "это то же соревнование, что у нас уже затянуто";
        if (logligId <= 0 || Math.Abs(logligId - bound) > ProgrammeCheckRules.NeighbourRadius)
            return "это соревнование на loglig не рядом с нашим — похоже, чужое";
        if (candidate == null)
            return "на loglig такого соревнования нет";
        var key = LogligTitleRules.Normalize(boundTitle);
        if (key.Length == 0)
            return "не удалось прочитать название нашего соревнования на loglig — попробуйте позже";
        if (LogligTitleRules.Normalize(candidate.Title) != key)
            return $"на loglig оно называется «{candidate.Title}», а не как наше — похоже, это другое соревнование";

        var missing = ProgrammeCheckRules.MissingSiteDays(siteStart, siteEnd, dbDays);
        if (missing.Count == 0)
            return "у нас уже есть все дни — затягивать нечего";
        var dates = ProgrammeCheckRules.NeighbourDates(candidate);
        if (dates.Count == 0)
            return "у соревнования на loglig нет даты — не понять, какой это день";
        if (dates.Count > 1)
            return $"у соревнования на loglig несколько дат ({Days(dates)}) — кнопка затягивает только один день";
        if (!missing.Contains(dates[0]))
            return dbDays.Contains(dates[0])
                ? $"день {Day(dates[0])} у нас уже есть"
                : $"его дата {Day(dates[0])} — не тот день, которого не хватает ({Days(missing)})";
        return null;
    }

    /// <summary>
    /// Дата разобранного протокола: ровно одна, в недостающем дне, того же числа, что у соседа на
    /// loglig, и такого дня в базе ещё нет. null — годится.
    /// </summary>
    public static string? FileDateRefusal(IEnumerable<string?> fileDates, IReadOnlyList<DateOnly> candidateDates,
        IReadOnlyList<DateOnly> missing, IReadOnlyList<DateOnly> dbDays)
    {
        var raw = fileDates.Select(d => d?.Trim() ?? "").Distinct().ToList();
        var parsed = raw.Select(ParseDate).ToList();
        if (raw.Count == 0) return "в результатах нет ни одного дня";
        if (parsed.Any(d => d == null)) return $"в результатах непонятная дата: {string.Join(", ", raw)}";
        var dates = parsed.Select(d => d!.Value).Distinct().Order().ToList();
        if (dates.Count != 1) return $"в результатах {dates.Count} даты ({Days(dates)}) — кнопка затягивает только один день";
        var date = dates[0];
        if (dbDays.Contains(date)) return $"день {Day(date)} у нас уже есть";
        if (!missing.Contains(date)) return $"в результатах дата {Day(date)} — не тот день, которого не хватает ({Days(missing)})";
        if (candidateDates.Count > 0 && !candidateDates.Contains(date))
            return $"в результатах дата {Day(date)}, а у соревнования на loglig — {Days(candidateDates)}";
        return null;
    }

    /// <summary>Заплывы файла, которые уже лежат в днях старта (тот же человек, дисциплина, заплыв и дорожка).</summary>
    public static IReadOnlyList<SiblingSwimKey> Overlap(IEnumerable<SiblingSwimKey> fileRows, IEnumerable<SiblingSwimKey> dbRows)
    {
        // Без заплыва/дорожки (0) и без имени сравнивать нечего: такие строки совпали бы у всех.
        static bool Usable(SiblingSwimKey k) => k.Heat > 0 && k.Lane > 0 && k.Person.Length > 0;
        var db = dbRows.Where(Usable).ToHashSet();
        return fileRows.Where(Usable).Distinct().Where(db.Contains).ToList();
    }

    /// <summary>
    /// Сверка «тот же ли это протокол» с запасным ключом. Если у большинства личных строк файла есть заплыв
    /// и дорожка — ключ «человек + дисциплина + заплыв + дорожка»; иначе (протокол без заплывов и дорожек) —
    /// «человек + дисциплина + время»; нет и времени — сверять нечем (<see cref="OverlapBy.None"/>, превью
    /// предупреждает, запись не блокирует: решает человек, глядя на дату и строки).
    /// </summary>
    public static OverlapResult OverlapCheck(IReadOnlyList<SiblingSwim> file, IReadOnlyList<SiblingSwim> db)
    {
        var people = file.Where(r => r.Person.Length > 0).ToList();
        if (people.Count == 0) return new(OverlapBy.None, [], file.Count, null);

        static bool HeatLane(SiblingSwim r) => r.Heat > 0 && r.Lane > 0;
        static bool Timed(SiblingSwim r) => r.TimeMs is > 0;
        OverlapBy by = people.Count(HeatLane) * 2 >= people.Count ? OverlapBy.HeatLane
            : people.Count(Timed) * 2 >= people.Count ? OverlapBy.Time
            : OverlapBy.None;
        if (by == OverlapBy.None) return new(OverlapBy.None, [], file.Count, null);

        Func<SiblingSwim, bool> usable = by == OverlapBy.HeatLane ? HeatLane : Timed;
        Func<SiblingSwim, object> key = by == OverlapBy.HeatLane
            ? r => (r.Person, r.BirthYear, r.Style, r.Distance, r.Heat, r.Lane)
            : r => (r.Person, r.BirthYear, r.Style, r.Distance, r.TimeMs);
        var dbByKey = db.Where(r => r.Person.Length > 0 && usable(r)).GroupBy(key).ToDictionary(g => g.Key, g => g.ToList());
        var matched = people.Where(usable).DistinctBy(key).Where(r => dbByKey.ContainsKey(key(r))).ToList();
        var days = matched.SelectMany(r => dbByKey[key(r)]).Select(r => r.Day).OfType<DateOnly>().Distinct().ToList();
        return new(by, matched, file.Count, days.Count == 1 ? days[0] : null);
    }

    /// <summary>
    /// Отказ по пересечению: совпало не меньше <see cref="OverlapMinRows"/> и не меньше
    /// <see cref="OverlapShare"/> личных строк файла. null — дни разные (или сверять было нечем).
    /// </summary>
    public static string? OverlapRefusal(OverlapResult r) =>
        r.By == OverlapBy.None ? null
            : OverlapRefusal(r.Matched.Count, r.FileRows, r.InDay,
                r.By == OverlapBy.HeatLane ? "тот же пловец, дисциплина, заплыв и дорожка" : "тот же пловец, дисциплина и время");

    /// <param name="inDay">День, где лежат совпавшие заплывы (если он один) — «уже есть в пятнице 06.12».</param>
    public static string? OverlapRefusal(IReadOnlyList<SiblingSwimKey> overlap, int fileIndividualRows, DateOnly? inDay = null) =>
        OverlapRefusal(overlap.Count, fileIndividualRows, inDay, "тот же пловец, дисциплина, заплыв и дорожка");

    private static string? OverlapRefusal(int matched, int fileIndividualRows, DateOnly? inDay, string how)
    {
        var threshold = Math.Max(OverlapMinRows, (int)Math.Ceiling(fileIndividualRows * OverlapShare));
        if (matched < threshold) return null;
        var where = inDay is { } d ? $"в {MissingDayTexts.Prep(d)} {MissingDayTexts.Day(d)}" : "в днях, что у нас уже есть";
        return $"эти заплывы уже есть {where} — похоже, это тот же протокол, а не другой день "
            + $"(совпало {matched} из {fileIndividualRows}: {how})";
    }

    /// <summary>Ключ человека: фамилия и имя без пробелов, знаков и регистра.</summary>
    public static string Person(string? lastName, string? firstName)
    {
        var sb = new StringBuilder();
        foreach (var ch in $"{lastName}|{firstName}")
            if (char.IsLetterOrDigit(ch) || ch == '|') sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString() == "|" ? "" : sb.ToString();
    }

    /// <summary>
    /// Бассейн по умолчанию — от лежащего дня. Регламент кандидата говорит другое — подставляем всё равно
    /// длину лежащего дня (у дней события она одна, сервер примет только её) и поднимаем <c>Conflict</c>:
    /// превью прямо говорит, что если неверен лежащий день — сначала исправить его (И-30).
    /// </summary>
    public static (string? Pool, bool Conflict) DefaultPool(string? existingPool, string? regulationPool)
    {
        var existing = NormalizePool(existingPool);
        var regulation = NormalizePool(regulationPool);
        if (existing == null) return (regulation, false);
        if (regulation == null || regulation == existing) return (existing, false);
        return (existing, true); // сервер примет только длину лежащего дня — её и подставляем, с предупреждением
    }

    public static string? NormalizePool(string? pool)
    {
        var p = pool?.Trim().ToLowerInvariant().TrimEnd('m');
        return p is "25" or "50" ? p + "m" : null;
    }

    private static DateOnly? ParseDate(string date) =>
        DateOnly.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string Day(DateOnly d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    private static string Days(IEnumerable<DateOnly> ds) => string.Join(", ", ds.Select(Day));
}
