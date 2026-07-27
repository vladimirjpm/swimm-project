namespace Swimm.Infrastructure.Services;

/// <summary>
/// Серверный расчёт объединённого зачёта «Combine All Results» — порт клиентского
/// <c>recalculatePositions</c> (client/src/utils/helpers/recalculate-positions.ts).
///
/// Смысл: пловцы дисциплины ранжируются по лучшему времени поверх ВСЕХ дней и заплывов
/// события. Протокольное место — это место внутри своего заплыва/дня/категории;
/// объединённое — место в общем зачёте дисциплины.
///
/// Отличие от клиента: пловец матчится по <c>SwimmerId</c>, а не по строке
/// «имя|фамилия|год|клуб» — точнее там, где один пловец сменил клуб между днями или
/// в данных есть тёзки.
///
/// Чистая функция: I/O (загрузка строк, сохранение результата) — на вызывающем.
/// </summary>
public static class CombinedPlaceCalculator
{
    /// <summary>Минимальная проекция заплыва для расчёта.</summary>
    public sealed record Row(
        long ResultId,
        int SwimmerId,
        /// <summary>Ключ дисциплины: стиль + дистанция + бассейн + пол + возрастная группа события.</summary>
        string EventKey,
        int? TimeMilliseconds,
        bool TimeFail);

    /// <summary>Результат расчёта для одной строки.</summary>
    public sealed record Assignment(long ResultId, int? CombinedPlace, bool IsBestResult, int? BestTimeMs);

    /// <summary>
    /// Считает объединённые места для всех строк события.
    /// Незачтённые заплывы (<c>TimeFail</c> или без времени) места не получают, но
    /// возвращаются с null — чтобы вызывающий мог обнулить им ранее рассчитанные значения.
    /// </summary>
    public static List<Assignment> Calculate(IReadOnlyCollection<Row> rows)
    {
        var result = new List<Assignment>(rows.Count);

        foreach (var group in rows.GroupBy(r => r.EventKey))
        {
            // Лучшее (минимальное) время каждого пловца в дисциплине за всё событие.
            var best = new Dictionary<int, (int TimeMs, long ResultId)>();
            foreach (var r in group)
            {
                if (!IsScorable(r)) continue;

                var timeMs = r.TimeMilliseconds!.Value;
                if (!best.TryGetValue(r.SwimmerId, out var current) || timeMs < current.TimeMs)
                    best[r.SwimmerId] = (timeMs, r.ResultId);
            }

            // Ранжирование пловцов по лучшему времени. Одинаковое время → одинаковое место
            // (клиент этого не делает — раздаёт места по порядку сортировки; здесь честнее,
            // и совпадает с тем, как ничьи трактуются в протоколах).
            var places = new Dictionary<int, int>();
            var ordered = best.OrderBy(kv => kv.Value.TimeMs).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var (swimmerId, entry) = (ordered[i].Key, ordered[i].Value);
                places[swimmerId] = i > 0 && ordered[i - 1].Value.TimeMs == entry.TimeMs
                    ? places[ordered[i - 1].Key]
                    : i + 1;
            }

            foreach (var r in group)
            {
                var hasBest = best.TryGetValue(r.SwimmerId, out var swimmerBest);
                result.Add(new Assignment(
                    ResultId: r.ResultId,
                    CombinedPlace: IsScorable(r) && places.TryGetValue(r.SwimmerId, out var place) ? place : null,
                    IsBestResult: hasBest && swimmerBest.ResultId == r.ResultId,
                    BestTimeMs: hasBest ? swimmerBest.TimeMs : null));
            }
        }

        return result;
    }

    /// <summary>Ключ дисциплины — как <c>buildEventKey</c> на клиенте.</summary>
    public static string EventKeyOf(int styleId, string distance, string poolType, string gender, string eventStyleAge)
        => string.Join('|', styleId, distance ?? "", poolType ?? "", gender ?? "", eventStyleAge ?? "");

    private static bool IsScorable(Row r) => !r.TimeFail && r.TimeMilliseconds is > 0;
}
