using Swimm.Domain;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Чистый расчёт клубных очков за место по правилам <see cref="PointRuleClubs"/> —
/// выделен из <c>HubGroupPublicRepository</c> ради юнит-тестируемости (сезонный зачёт 8.5).
/// Выбор правила живёт в <see cref="CompetitionRuleResolver"/> (привязка по Id важнее
/// подбора по дате); здесь — только начисление по уже выбранному правилу.
/// </summary>
public static class PointRulesClubsScoring
{
    /// <summary>
    /// Очки за один заплыв по КОНКРЕТНОМУ правилу. Незачтённое время
    /// (<paramref name="timeFail"/>), отсутствие правила или некорректное место → 0.
    /// Эстафетный множитель здесь НЕ применяется — он в <see cref="RelayPointsFor"/>.
    /// </summary>
    public static int PointsFor(PointRuleClubs? rule, int? position, bool timeFail)
    {
        if (rule is null || timeFail || position is null || position < 1) return 0;

        if (rule.MaxScoringPlace is int max && position > max) return rule.DefaultPoints;
        var entry = rule.Entries.FirstOrDefault(e => e.Place == position.Value);
        return entry?.Points ?? rule.DefaultPoints;
    }

    /// <summary>Очки за заплыв с учётом эстафетного множителя правила
    /// (регламент бугрим п.17 «ניקוד כפול» — раньше был хардкод *2).</summary>
    public static int RelayPointsFor(PointRuleClubs? rule, int? position, bool timeFail, bool isRelay)
    {
        var points = PointsFor(rule, position, timeFail);
        return isRelay && rule is not null ? points * rule.RelayMultiplier : points;
    }

    /// <summary>
    /// Совместимая перегрузка: сама подбирает правило по дате/области. Используется там,
    /// где привязки соревнования нет под рукой (и в тестах подбора).
    /// </summary>
    public static int PointsFor(
        IReadOnlyCollection<PointRuleClubs> rules, int? position, bool timeFail, bool isMasters, DateOnly date)
    {
        if (timeFail || position is null || position < 1) return 0;
        return PointsFor(SelectRule(rules, isMasters, date), position, timeFail);
    }

    /// <summary>Действующее на дату правило нужной области (masters/non-masters), fallback на "all".</summary>
    public static PointRuleClubs? SelectRule(IReadOnlyCollection<PointRuleClubs> rules, bool isMasters, DateOnly date)
        => CompetitionRuleResolver.SelectByDate(rules, isMasters, date);

    // ── Зачёт «по заплыву целиком» (схема Красного моря) ─────────────────────────────────

    /// <summary>
    /// Правилу нужен весь заплыв, а не одна строка: общая категория, порог участников или
    /// список зачётных дистанций. Такие правила считает <see cref="PointsForSet"/> (через
    /// <see cref="ContextClubPoints"/>); остальные — построчно, как всегда.
    /// </summary>
    public static bool NeedsContext(PointRuleClubs? rule) =>
        rule is not null
        && (rule.OverallMultiplier is not null || rule.MinEntrants is not null
            || !string.IsNullOrWhiteSpace(rule.Distances) || !string.IsNullOrWhiteSpace(rule.AgeBands));

    /// <summary>
    /// Очки каждой строки одного соревнования по правилу «по заплыву целиком» — схема
    /// «אליפות הים האדום» (регламент Эйлата 2025/2026: возрастные категории 3/2/1, общая
    /// категория 6/4/2, медали при ≥ 3 участниках, зачёт только в заплыве 1.66 км).
    ///
    /// <list type="number">
    /// <item>Дистанция вне <see cref="PointRuleClubs.Distances"/> — 0.</item>
    /// <item>Категория (дисциплина + программа протокола): место — ПРОТОКОЛЬНОЕ, очки по шкале
    /// как обычно; при <see cref="PointRuleClubs.MinEntrants"/> категория, где стартовало
    /// меньше, не приносит ничего. При общей категории личная секция «כללי» (mix-…/open)
    /// возрастной не считается: у loglig это остаток «20+», а по регламенту общая категория —
    /// все возрасты.</item>
    /// <item>Общая категория (<see cref="PointRuleClubs.OverallMultiplier"/>): абсолютное место
    /// по времени среди всех личных строк дисциплины (тот же пол), делёж при равном времени;
    /// очки шкалы × вес — СВЕРХ очков категории (победитель возраста и абсолюта получает оба).</item>
    /// </list>
    /// Дисциплина — <see cref="ClubPointsContextRow.Discipline"/> (стиль|дистанция|пол).
    /// </summary>
    public static IReadOnlyDictionary<long, int> PointsForSet(PointRuleClubs rule, IEnumerable<ClubPointsContextRow> rows)
    {
        var all = rows.ToList();
        var points = all.ToDictionary(r => r.Id, _ => 0);
        var distances = ParseDistances(rule.Distances);
        var scored = all.Where(r => distances is null || distances.Contains(r.Distance.Trim())).ToList();

        var bands = ParseAgeBands(rule.AgeBands);
        if (bands is not null)
        {
            // Категории строит правило: полоса по возрасту, место — по времени внутри полосы.
            foreach (var band in scored
                         .Where(r => !r.IsRelay && r.Awarding)
                         .Select(r => (Row: r, Band: bands.FirstOrDefault(b => r.Age >= b.Min && r.Age <= b.Max)))
                         .Where(x => x.Band != default)
                         .GroupBy(x => (x.Row.Discipline, x.Band), x => x.Row))
            {
                if (rule.MinEntrants is int min && band.Count(Started) < min) continue;

                var timed = band.Where(r => !r.TimeFail && r.TimeMs > 0).ToList();
                foreach (var r in timed)
                    points[r.Id] = PointsFor(rule, 1 + timed.Count(o => o.TimeMs < r.TimeMs), timeFail: false);
            }
        }

        foreach (var category in scored
                     .Where(r => bands is null || r.IsRelay)
                     .GroupBy(r => (r.Discipline, r.IsRelay, Category: r.Category ?? "")))
        {
            if (rule.MinEntrants is int min && category.Count(Started) < min) continue;
            if (rule.OverallMultiplier is not null && !category.Key.IsRelay && IsOpenSection(category.Key.Category))
                continue;

            foreach (var r in category)
                points[r.Id] = RelayPointsFor(rule, r.Position, r.TimeFail, r.IsRelay);
        }

        if (rule.OverallMultiplier is int weight)
        {
            foreach (var discipline in scored.Where(r => !r.IsRelay && r.Awarding).GroupBy(r => r.Discipline))
            {
                if (rule.MinEntrants is int min && discipline.Count(Started) < min) continue;

                var timed = discipline.Where(r => !r.TimeFail && r.TimeMs > 0).ToList();
                foreach (var r in timed)
                {
                    var place = 1 + timed.Count(o => o.TimeMs < r.TimeMs);
                    points[r.Id] += PointsFor(rule, place, timeFail: false) * weight;
                }
            }
        }

        return points;
    }

    /// <summary>Стартовал ли пловец: неявка (NS/DNS) участником категории не считается, DQ и DNF — считаются.</summary>
    private static bool Started(ClubPointsContextRow r) => !StartStatus.IsNoStart(r.TimeFail, r.FailNote);

    /// <summary>Секция «כללי» протокола: парсер метит её программой mix-… (пола нет) или open.</summary>
    private static bool IsOpenSection(string category) =>
        category.StartsWith("mix", StringComparison.OrdinalIgnoreCase)
        || category.Equals("open", StringComparison.OrdinalIgnoreCase);

    /// <summary>«12-13,14-15» → [(12,13),(14,15)]; одиночное «17» — полоса из одного возраста.
    /// Пусто — null; кривой кусок — исключение (форма админки валидирует заранее).</summary>
    public static IReadOnlyList<(int Min, int Max)>? ParseAgeBands(string? bands)
    {
        if (string.IsNullOrWhiteSpace(bands)) return null;
        return bands.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part =>
            {
                var ends = part.Split('-', StringSplitOptions.TrimEntries);
                var min = int.Parse(ends[0], System.Globalization.CultureInfo.InvariantCulture);
                var max = ends.Length > 1 ? int.Parse(ends[1], System.Globalization.CultureInfo.InvariantCulture) : min;
                if (ends.Length > 2 || min <= 0 || max < min)
                    throw new FormatException($"Возрастная полоса «{part}» не в формате «12-13»");
                return (min, max);
            })
            .ToList();
    }

    private static HashSet<string>? ParseDistances(string? distances)
    {
        if (string.IsNullOrWhiteSpace(distances)) return null;
        return distances.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>Строка заплыва для <see cref="PointRulesClubsScoring.PointsForSet"/>.</summary>
/// <param name="Discipline">Ключ дисциплины внутри соревнования: стиль|дистанция|пол.</param>
/// <param name="Category">Программа протокола (<c>Results.EventCategory</c>).</param>
/// <param name="Position">Место, уже очищенное от незачётных раундов (prelim, «כללי»-финал бассейна).</param>
/// <param name="Awarding">Раунд награждаемый (не prelim/extra) — участвует в абсолютном зачёте.</param>
/// <param name="Age">Возраст в сезоне (<c>SeasonMath.AgeInSeason</c>) — для полос <see cref="PointRuleClubs.AgeBands"/>.</param>
public sealed record ClubPointsContextRow(
    long Id, string Discipline, string Distance, string? Category,
    int? Position, int? TimeMs, bool TimeFail, string? FailNote, bool IsRelay, bool Awarding = true,
    int? Age = null);
