using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Рекорды двух стран → сравнение по дисциплинам (этап 11.3.1).
///
/// Чистая функция по той же причине, что <see cref="RecordRankingBuilder"/>: выравнивание по
/// общей оси и счёт — то, что ломается молча. Здесь молчание особенно дорого: главное правило
/// этапа (11.3.3) в том, что **«нет данных» ≠ «медленнее»**, и нарушить его можно одной
/// строкой кода, а заметить — только сравнив две страны с разным покрытием.
/// </summary>
public static class RecordCompareBuilder
{
    /// <summary>Строка справочника на входе: ось дисциплины + сам рекорд.</summary>
    public sealed record Row(
        string RegionCode,
        string Style,
        string Distance,
        string Gender,
        string PoolType,
        string Time,
        int TimeMs,
        string? HolderName,
        string? RecordDate,
        string? IssueReason);

    private sealed record Axis(string Style, string Distance, string Gender, string PoolType);

    /// <summary>
    /// Собирает сравнение. Порядок строк детерминированный — стиль, дистанция, пол, бассейн;
    /// эстафеты идут после личных дистанций того же стиля.
    /// </summary>
    /// <param name="rows">Строки ОБЕИХ стран, как их отдала база (порядок не важен).</param>
    public static RecordCompareDto Build(IReadOnlyList<Row> rows, string a, string b)
    {
        var byAxis = new Dictionary<Axis, (Row? A, Row? B)>();

        foreach (var r in rows)
        {
            var axis = new Axis(r.Style, r.Distance, r.Gender, r.PoolType);
            byAxis.TryGetValue(axis, out var pair);

            // Своя страна у строки определяется КОДОМ, а не порядком в выборке: у сравнения
            // страны с самой собой (A == B) обе стороны — одна и та же строка, и это
            // законный, хоть и бессмысленный, ответ.
            if (string.Equals(r.RegionCode, a, StringComparison.OrdinalIgnoreCase)) pair.A = r;
            if (string.Equals(r.RegionCode, b, StringComparison.OrdinalIgnoreCase)) pair.B = r;

            byAxis[axis] = pair;
        }

        var score = new RecordCompareScoreDto();
        var result = new List<RecordCompareRowDto>(byAxis.Count);

        foreach (var (axis, pair) in byAxis.OrderBy(kv => kv.Key.Style, StringComparer.Ordinal)
                     .ThenBy(kv => DistanceOrder(kv.Key.Distance))
                     .ThenBy(kv => kv.Key.Gender, StringComparer.Ordinal)
                     .ThenBy(kv => kv.Key.PoolType, StringComparer.Ordinal))
        {
            var (rowA, rowB) = pair;

            // Обе стороны есть — только тогда это сравнение. Одна сторона — «нет данных»,
            // и в счёт такая дисциплина не идёт НИ В ЧЬЮ пользу (11.3.3).
            var outcome = (rowA, rowB) switch
            {
                (null, null) => RecordCompareOutcome.NoData,
                (not null, null) => RecordCompareOutcome.NoData,
                (null, not null) => RecordCompareOutcome.NoData,
                var (x, y) when x!.TimeMs < y!.TimeMs => RecordCompareOutcome.A,
                var (x, y) when x!.TimeMs > y!.TimeMs => RecordCompareOutcome.B,
                _ => RecordCompareOutcome.Tie,
            };

            if (rowA != null && rowB != null)
            {
                score.Compared++;
                if (outcome == RecordCompareOutcome.A) score.A++;
                else if (outcome == RecordCompareOutcome.B) score.B++;
                else if (outcome == RecordCompareOutcome.Tie) score.Tie++;
            }
            else if (rowA != null) score.AOnly++;
            else if (rowB != null) score.BOnly++;

            result.Add(new RecordCompareRowDto
            {
                Style = axis.Style,
                Distance = axis.Distance,
                Gender = axis.Gender,
                PoolType = axis.PoolType,
                A = Side(rowA),
                B = Side(rowB),
                Outcome = OutcomeKey(outcome),
                DeltaMs = rowA != null && rowB != null ? Math.Abs(rowA.TimeMs - rowB.TimeMs) : null,
            });
        }

        return new RecordCompareDto
        {
            A = a,
            B = b,
            Score = score,
            Rows = result,
        };
    }

    /// <summary>
    /// Ключ исхода в JSON — строкой и в нижнем регистре. Числовой enum по умолчанию уже
    /// ломал вкладку прогона по странам молча (см. `RecordCountryRunState`), повторять не будем.
    /// </summary>
    public static string OutcomeKey(RecordCompareOutcome outcome) => outcome switch
    {
        RecordCompareOutcome.A => "a",
        RecordCompareOutcome.B => "b",
        RecordCompareOutcome.Tie => "tie",
        _ => "no_data",
    };

    private static RecordCompareSideDto? Side(Row? row) => row == null ? null : new RecordCompareSideDto
    {
        Time = row.Time,
        TimeMs = row.TimeMs,
        HolderName = row.HolderName,
        RecordDate = row.RecordDate,
        IssueReason = row.IssueReason,
    };

    /// <summary>
    /// Порядок дистанций: 50, 100, 200 … и эстафеты ПОСЛЕ личных того же стиля. Сортировать
    /// строкой нельзя — «1500m» встало бы между «100m» и «200m», а «4X50m» первым.
    /// </summary>
    private static int DistanceOrder(string distance)
    {
        var relay = distance.StartsWith("4X", StringComparison.OrdinalIgnoreCase);
        var digits = new string(distance.Where(char.IsAsciiDigit).ToArray());
        if (relay && digits.Length > 1) digits = digits[1..];   // «4X100m» → 100, а не 4100
        var meters = int.TryParse(digits, out var n) ? n : 0;
        return (relay ? 100_000 : 0) + meters;
    }
}
