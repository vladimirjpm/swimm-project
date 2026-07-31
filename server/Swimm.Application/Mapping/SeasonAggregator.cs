using Swimm.Domain;

namespace Swimm.Application.Mapping;

/// <summary>
/// Минимальная проекция заплыва для сезонных агрегатов. Чистые данные — I/O делает репозиторий.
/// <paramref name="PoolType"/> живёт у соревнования, поэтому репозиторий обязан его подтянуть
/// (25m и 50m — разные дисциплины, времена несравнимы).
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
    bool IsRelay);

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
    public static string DisciplineKey(SeasonSwimRow row, bool includeEventCategory = false)
    {
        var dist = Norm(row.Distance).TrimEnd('m');
        var key = $"{row.StyleId}|{dist}|{Norm(row.PoolType)}|{Norm(row.Gender)}";
        return includeEventCategory ? $"{key}|{Norm(row.EventCategory)}" : key;
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
