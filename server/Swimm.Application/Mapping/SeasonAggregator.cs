using Swimm.Domain;

namespace Swimm.Application.Mapping;

/// <summary>
/// Проекция заплыва для сезонных агрегатов. Чистые данные — I/O делает репозиторий.
/// <paramref name="PoolType"/> живёт у соревнования, поэтому репозиторий обязан его подтянуть
/// (25m и 50m — разные дисциплины, времена несравнимы).
///
/// В конструкторе — минимум, которого хватает арифметике сезона (best/PB/сезоны). Всё, что
/// нужно только для ОТРИСОВКИ строки результата, добавлено `init`-свойствами ниже: страница
/// спортсмена показывает место, очки, сплиты и соревнование, и заводить ради них вторую
/// проекцию нельзя — через полгода будет два способа считать сезон (плана athlete-page §A1).
/// Незаполненные свойства безопасны: агрегаты их не читают.
/// </summary>
public sealed record SeasonSwimRow(
    long ResultId,
    int SwimmerId,
    int CompetitionId,
    DateTime CompetitionDate,
    int StyleId,
    string Distance,
    string Gender,
    string PoolType,
    string? EventCategory,
    int? TimeMilliseconds,
    bool TimeFail,
    string? SuspectReason,
    bool IsRelay)
{
    /// <summary>Место в протоколе своего заплыва — ОФИЦИАЛЬНОЕ, за него вручена медаль.</summary>
    public int? Position { get; init; }

    /// <summary>prelim / final / null (timed final или данные без признака). Место
    /// prelim-заплыва — ранжир сессии, не награда: медали считаются без него.</summary>
    public string? HeatType { get; init; }

    /// <summary>Место внутри возрастной полосы протокола (грубее заплыва).</summary>
    public int? PositionAgeGroup { get; init; }

    /// <summary>Очки FINA за заплыв. Сравнимы между стартами только внутри одной таблицы очков.</summary>
    public int InternationalPoints { get; init; }

    /// <summary>Время как напечатано в протоколе — единственное, что показывает `UI_SwimTime`.</summary>
    public string? TimeOriginal { get; init; }

    /// <summary>Сплиты строкой из протокола.</summary>
    public string? TimeSplit { get; init; }

    /// <summary>Событие многодневки: все дни делят один <c>EventId</c> — по нему считается
    /// «сколько соревнований», иначе трёхдневный старт станет тремя.</summary>
    public int? EventId { get; init; }

    public string? CompetitionName { get; init; }

    /// <summary>Единственный источник значка 🏆 — по названию чемпионат не определяется.</summary>
    public bool IsChampionship { get; init; }

    /// <summary>Медали вручались (<c>Competition.IsAward</c>): без него место — просто место.</summary>
    public bool IsAward { get; init; }

    public bool IsMasters { get; init; }

    /// <summary>Ключ стиля как на клиенте (freestyle/backstroke/…) — по нему рисуется плита стиля.</summary>
    public string? StyleName { get; init; }

    /// <summary>Клуб НА МОМЕНТ заплыва: пловец переходит между клубами, и история это помнит.</summary>
    public int ClubId { get; init; }

    /// <summary>Возрастная полоса протокола («9-10»).</summary>
    public string? AgeGroup { get; init; }

    /// <summary>Возраст события — настоящая ось заплыва (см. <c>ResultRecord.EventStyleAge</c>).</summary>
    public string? EventStyleAge { get; init; }
}

/// <summary>
/// Официальный рекорд страны по возрастной ступени — вход для колонки «Δ Israel {age}».
/// <paramref name="TimeMs"/> считается парсингом строки: у <c>Record</c> времени в
/// миллисекундах нет (открытое решение №3 в records-all-countries-plan).
/// <paramref name="IssueReason"/> обязателен по инварианту И11: раз тут показано время,
/// рядом должен быть признак его качества — справочник рекордов тоже ошибается
/// (<c>Sys_RecordIssues</c>, качество <c>record</c>).
/// </summary>
public sealed record NationalAgeRecordRow(
    string Time, int? TimeMs, string? Holder, string AgeKey, string? IssueReason = null);

/// <summary>
/// Общий сезонный шов страниц спортсмена и клуба (фаза 10.1): «результаты → сезоны»,
/// лучшее в сезоне и детекция личных рекордов. Считать это дважды по-разному нельзя —
/// поэтому один хелпер на обе страницы.
///
/// Границы сезона берутся из <see cref="SeasonMath"/> (1 сен – 31 авг, метка по году начала).
///
/// Из best/PB исключаются:
/// <list type="bullet">
/// <item><c>TimeFail</c> — DSQ/DNS/DNF и всё без времени;</item>
/// <item><c>SuspectReason</c> — помеченные ошибки САМОГО протокола (см. result-quality-suspect):
/// такой заплыв остаётся в протоколе, но «рекордом» считаться не должен;</item>
/// <item>эстафетные строки — нога эстафеты не сравнима с личным стартом.</item>
/// </list>
/// </summary>
public static class SeasonAggregator
{
    /// <summary>Заплыв годится для best/PB (не DSQ, не подозрительный, не эстафета, есть время).</summary>
    public static bool IsCountable(SeasonSwimRow row) =>
        !row.TimeFail
        && !row.IsRelay
        && row.SuspectReason is null
        && row.TimeMilliseconds is > 0;

    /// <summary>
    /// Ключ дисциплины: стиль × дистанция × <b>бассейн</b> × пол.
    /// <paramref name="includeEventCategory"/> добавляет категорию заплыва (<c>open</c>/<c>para</c>/
    /// <c>mix</c>/возрастная) — нужна на странице спортсмена, иначе три золота Маккабиады в одной
    /// дисциплине сливаются в одно. В клубном зачёте <c>EventCategory</c> сознательно не учитывается.
    /// </summary>
    public static string DisciplineKey(SeasonSwimRow row, bool includeEventCategory = false) =>
        DisciplineKey(row.StyleId, row.Distance, row.PoolType, row.Gender,
            includeEventCategory ? row.EventCategory : null, includeEventCategory);

    /// <summary>
    /// Тот же ключ, собранный из частей — нужен, чтобы сравнивать заплывы со СПРАВОЧНИКОМ
    /// рекордов, где дистанция записана иначе («50m» против «50»), а стиль строкой.
    /// Нормализация обязана быть одна на оба источника, иначе сравнение молча не найдёт пару.
    /// </summary>
    public static string DisciplineKey(
        int styleId, string? distance, string? poolType, string? gender,
        string? eventCategory = null, bool includeEventCategory = false)
    {
        var dist = Norm(distance).TrimEnd('m');
        var key = $"{styleId}|{dist}|{Norm(poolType)}|{Norm(gender)}";
        return includeEventCategory ? $"{key}|{Norm(eventCategory)}" : key;
    }

    /// <summary>Год начала сезона, которому принадлежит заплыв.</summary>
    public static int SeasonOf(SeasonSwimRow row) => SeasonMath.StartYearOf(row.CompetitionDate);

    /// <summary>
    /// Лучший заплыв в каждой паре (сезон × дисциплина). Незачётные строки игнорируются;
    /// при равном времени побеждает более ранний (первым показанный результат).
    /// </summary>
    public static Dictionary<(int Season, string Discipline), SeasonSwimRow> SeasonBests(
        IEnumerable<SeasonSwimRow> rows, bool includeEventCategory = false)
    {
        var best = new Dictionary<(int, string), SeasonSwimRow>();
        foreach (var row in rows)
        {
            if (!IsCountable(row)) continue;
            var key = (SeasonOf(row), DisciplineKey(row, includeEventCategory));
            if (!best.TryGetValue(key, out var cur) || IsBetter(row, cur))
                best[key] = row;
        }
        return best;
    }

    /// <summary>
    /// Заплывы, которые на момент старта были личным рекордом пловца в своей дисциплине.
    /// Первый зачётный заплыв в дисциплине — уже PB (улучшать пока нечего).
    /// Повтор того же времени рекордом НЕ считается — нужно строго быстрее.
    /// </summary>
    public static HashSet<long> PersonalBests(
        IEnumerable<SeasonSwimRow> rows, bool includeEventCategory = false)
    {
        var ordered = rows
            .Where(IsCountable)
            .OrderBy(r => r.CompetitionDate)
            .ThenBy(r => r.ResultId);

        var bestSoFar = new Dictionary<(int Swimmer, string Discipline), int>();
        var pbs = new HashSet<long>();
        foreach (var row in ordered)
        {
            var key = (row.SwimmerId, DisciplineKey(row, includeEventCategory));
            var ms = row.TimeMilliseconds!.Value;
            if (bestSoFar.TryGetValue(key, out var prev) && ms >= prev) continue;
            bestSoFar[key] = ms;
            pbs.Add(row.ResultId);
        }
        return pbs;
    }

    /// <summary>Сезоны, в которых есть хоть один заплыв, — от свежих к старым (для селектора).</summary>
    public static List<int> SeasonsPresent(IEnumerable<SeasonSwimRow> rows) =>
        rows.Select(SeasonOf).Distinct().OrderByDescending(s => s).ToList();

    private static bool IsBetter(SeasonSwimRow candidate, SeasonSwimRow current) =>
        candidate.TimeMilliseconds!.Value < current.TimeMilliseconds!.Value;

    /// <summary>Нормализация части ключа: пусто/null → «», иначе trim + lower.</summary>
    private static string Norm(string? s) =>
        string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim().ToLowerInvariant();
}
