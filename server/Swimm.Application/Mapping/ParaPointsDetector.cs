namespace Swimm.Application.Mapping;

/// <summary>
/// Какие строки результатов несут очки по ПАРА-шкале, а не FINA (Р67, docs/data-integrity.md).
///
/// Очки FINA = 1000·(B/T)³, где B — базовое время дисциплины. Значит, внутри одной дисциплины
/// одного дня (стиль × дистанция × пол) произведение «очки × время³» у всех одинаково: по
/// базе на 29.09.2026 99,9% строк лежат в 0,97…1,01 от медианы. Loglig пара-пловцам считает
/// очки от базы их класса — и произведение улетает в разы (1:22.76 на 100 в/с → 931, 5,3×).
/// Базовые времена FINA при этом знать не нужно: эталон — соседи по дисциплине.
///
/// Пара — свойство ПЛОВЦА на соревновании, а не одного заплыва: источник считает по пара-шкале
/// все его заплывы, и часть из них выходит даже ниже нормы (0,4…0,7). Поэтому хватает одного
/// явного сигнала, чтобы пометить все его личные заплывы этого дня.
///
/// Пара-программа Маккабиады (<c>EventCategory = para</c>) помечается без расчёта и в эталон не
/// входит. Смешанные группы без пола (<c>mix-*</c>, пустой пол) в эталон не входят: у мужчин и
/// женщин разные базы, и разброс там естественный. Эстафеты не трогаем — очки команды.
/// </summary>
public static class ParaPointsDetector
{
    /// <summary>Во сколько раз «очки × время³» выше медианы дисциплины — явный сигнал пары.
    /// Обычные строки до 1,01; самый слабый пара-сигнал в базе — 1,23…1,46.</summary>
    public const double SignalRatio = 1.2;

    /// <summary>Меньше строк в дисциплине — эталону не верим.</summary>
    public const int MinGroupSize = 5;

    public sealed record Row(
        long ResultId,
        int CompetitionId,
        int SwimmerId,
        int StyleId,
        string Distance,
        string Gender,
        string? EventCategory,
        int Points,
        int? TimeMilliseconds,
        bool TimeFail,
        bool IsRelay);

    public static bool IsParaProgramme(string? eventCategory) =>
        string.Equals(eventCategory, "para", StringComparison.OrdinalIgnoreCase);

    /// <summary>Id строк, чьи очки — пара-шкала. Остальные строки входа — FINA.</summary>
    public static HashSet<long> Detect(IReadOnlyCollection<Row> rows)
    {
        var para = rows.Where(r => !r.IsRelay && IsParaProgramme(r.EventCategory))
            .Select(r => r.ResultId)
            .ToHashSet();

        var measurable = rows
            .Where(r => !r.IsRelay && !r.TimeFail && r.Points > 0 && r.TimeMilliseconds > 0
                        && r.Gender is "male" or "female"
                        && !IsParaProgramme(r.EventCategory)
                        && !(r.EventCategory ?? "").StartsWith("mix", StringComparison.OrdinalIgnoreCase))
            .Select(r => (Row: r, K: r.Points * Math.Pow(r.TimeMilliseconds!.Value / 1000.0, 3)));

        var paraSwimmers = new HashSet<(int CompetitionId, int SwimmerId)>();
        foreach (var group in measurable.GroupBy(x => (x.Row.CompetitionId, x.Row.StyleId, x.Row.Distance, x.Row.Gender)))
        {
            var list = group.ToList();
            if (list.Count < MinGroupSize) continue;
            var median = Median(list.Select(x => x.K));
            if (median <= 0) continue;
            foreach (var x in list.Where(x => x.K / median >= SignalRatio))
                paraSwimmers.Add((x.Row.CompetitionId, x.Row.SwimmerId));
        }

        foreach (var r in rows)
            if (!r.IsRelay && paraSwimmers.Contains((r.CompetitionId, r.SwimmerId)))
                para.Add(r.ResultId);

        return para;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}

/// <summary>
/// Очки результата как МЕЖДУНАРОДНЫЕ (FINA) — единственный вход для всего, что их показывает,
/// сравнивает или суммирует. Пара-очки (<see cref="ParaPointsDetector"/>) — 0: с FINA они не
/// сравнимы (Р67). В EF-запросах то же выражение пишется инлайном: <c>r.IsParaPoints ? 0 : r.InternationalPoints</c>.
/// </summary>
public static class ResultPoints
{
    public static int Fina(int points, bool isParaPoints) => isParaPoints ? 0 : points;
}
