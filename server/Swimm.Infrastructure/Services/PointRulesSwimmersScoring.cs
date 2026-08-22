using Swimm.Domain.Entities;
using Swimm.Domain;

namespace Swimm.Infrastructure.Services;

/// <summary>Побил ли заплыв возрастной рекорд своей категории.</summary>
public enum RecordStatus
{
    None = 0,
    /// <summary>Быстрее рекорда — установлен новый.</summary>
    Broken = 1,
    /// <summary>Ровно равно рекорду — повторён.</summary>
    Tied = 2
}

/// <summary>Заплыв в объёме, нужном для зачёта High Point.</summary>
public sealed record SwimmerHighPointRow(
    int SwimmerId,
    string Gender,
    int Age,
    string AgeGroup,
    int? Place,
    int InternationalPoints,
    bool IsRelay,
    bool TimeFail,
    RecordStatus Record = RecordStatus.None,
    string? HeatType = null);

/// <summary>Победитель номинации.</summary>
public sealed record SwimmerHighPointWinner(
    string Bucket,
    string Gender,
    int SwimmerId,
    int Age,
    string AgeGroup,
    int Points,
    int SwimCount,
    bool IsTie);

/// <summary>
/// Чистый расчёт High Point Swimmer по правилу <see cref="PointRuleSwimmers"/> — парный к
/// <see cref="PointRulesClubsScoring"/>. Выбор правила живёт в
/// <see cref="CompetitionRuleResolver"/>; здесь только начисление по уже выбранному.
///
/// Правило нужно потому, что регламенты принципиально разные (см. §8 плана):
///   • возрастные соревнования — очки за место (5/3/2/1) плюс замещающий бонус за
///     возрастной рекорд (13 за установленный, 8 за повторённый), номинации по возрасту;
///   • «бугрим» (взрослые) — сумма очков по международной таблице, один кубок на пол.
/// Одним хардкодом оба случая не покрыть.
/// </summary>
public static class PointRulesSwimmersScoring
{
    /// <summary>
    /// Очки за ОДИН заплыв. Рекорд ЗАМЕЩАЕТ очки за место, а не складывается с ними
    /// («כולל הניקוד עבור מדליית הזהב»): формально max(место, рекорд), и поскольку максимум
    /// за место (5) меньше бонуса (13/8), на практике это всегда очки за рекорд.
    ///
    /// Рекорд начисляется НЕЗАВИСИМО от места: возрастной рекорд принадлежит своей категории,
    /// а заплыв бывает сводным — 13-летний с рекордом для 13 лет может финишировать вторым
    /// за 14-летним, достижение от этого не исчезает.
    /// </summary>
    public static int PointsFor(PointRuleSwimmers? rule, SwimmerHighPointRow row)
    {
        if (rule is null || row.TimeFail) return 0;
        if (row.IsRelay && !rule.IncludeRelays) return 0;
        // FinalsOnly (бугрим п.16): предварительные заплывы очков не приносят. null-HeatType
        // проходит — это timed final либо данные без признака (тогда считаем по всем, как
        // до его появления; карточка показывает сноску FinalsOnlyUnavailable).
        if (rule.FinalsOnly && !HeatTypes.GivesOfficialPlace(row.HeatType)) return 0;

        if (rule.PointsSource == "fina") return row.InternationalPoints;

        var placePoints = PlacePoints(rule, row.Place);
        var recordPoints = row.Record switch
        {
            RecordStatus.Broken => rule.RecordPoints ?? 0,
            RecordStatus.Tied => rule.RecordTiePoints ?? 0,
            _ => 0
        };
        return Math.Max(placePoints, recordPoints);
    }

    private static int PlacePoints(PointRuleSwimmers rule, int? place)
    {
        if (place is null || place < 1) return 0;
        if (rule.MaxScoringPlace is int max && place > max) return rule.DefaultPoints;
        var entry = rule.Entries.FirstOrDefault(e => e.Place == place.Value);
        return entry?.Points ?? rule.DefaultPoints;
    }

    /// <summary>
    /// Победители номинаций. Номинация = корзина (<c>GroupBy</c>) × пол (если
    /// <c>SplitByGender</c>). Ничья по сумме → в результат попадают все с максимумом
    /// (<c>IsTie</c>), как в прежнем зашитом расчёте.
    ///
    /// <c>FinalsOnly</c>: предварительные заплывы (<c>HeatType == "prelim"</c>) исключаются
    /// целиком — не только из очков, но и из SwimCount/MinSwims. Строки с null-HeatType
    /// проходят: это timed final либо данные, импортированные до появления признака
    /// (тогда расчёт фактически идёт по всем заплывам — прежнее поведение варианта 3).
    /// </summary>
    public static List<SwimmerHighPointWinner> Winners(
        PointRuleSwimmers? rule, IReadOnlyCollection<SwimmerHighPointRow> rows)
    {
        if (rule is null || rows.Count == 0) return [];

        var scored = rows
            .Where(r => !r.TimeFail && (rule.IncludeRelays || !r.IsRelay))
            .Where(r => !rule.FinalsOnly || HeatTypes.GivesOfficialPlace(r.HeatType))
            .Select(r => (Row: r, Points: PointsFor(rule, r)))
            .ToList();

        var perSwimmer = scored
            .GroupBy(x => (Bucket: Bucket(rule, x.Row), x.Row.Gender, x.Row.SwimmerId))
            .Where(g => !string.IsNullOrEmpty(g.Key.Bucket))
            .Select(g =>
            {
                // CountBestSwims: в зачёт идут только N лучших заплывов пловца.
                var points = g.OrderByDescending(x => x.Points).AsEnumerable();
                if (rule.CountBestSwims is int take) points = points.Take(take);

                var first = g.First().Row;
                return new SwimmerHighPointWinner(
                    Bucket: g.Key.Bucket,
                    Gender: rule.SplitByGender ? g.Key.Gender : "",
                    SwimmerId: g.Key.SwimmerId,
                    Age: first.Age,
                    AgeGroup: first.AgeGroup,
                    Points: points.Sum(x => x.Points),
                    SwimCount: g.Count(),
                    IsTie: false);
            })
            .Where(x => rule.MinSwims is not int min || x.SwimCount >= min)
            .ToList();

        return perSwimmer
            .GroupBy(x => (x.Bucket, x.Gender))
            .SelectMany(g =>
            {
                var max = g.Max(x => x.Points);
                var winners = g.Where(x => x.Points == max).ToList();
                return winners.Select(x => x with { IsTie = winners.Count > 1 });
            })
            .OrderBy(x => x.Bucket).ThenBy(x => x.Gender)
            .ToList();
    }

    /// <summary>
    /// Корзина номинации: <c>age</c> — отдельный возраст, <c>age-group</c> — возрастная
    /// группа («25-29», как у masters), <c>none</c> — одна номинация на всех (кубок бугрим).
    /// </summary>
    private static string Bucket(PointRuleSwimmers rule, SwimmerHighPointRow row) => rule.GroupBy switch
    {
        "none" => "all",
        "age-group" => row.AgeGroup ?? "",
        _ => row.Age > 0 ? row.Age.ToString() : ""
    };
}
