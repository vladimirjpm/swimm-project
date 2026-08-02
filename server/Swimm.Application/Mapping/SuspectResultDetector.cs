using Swimm.Domain;

namespace Swimm.Application.Mapping;

/// <summary>Строка результата в объёме, нужном проверкам качества.</summary>
public sealed record SuspectCandidateRow(
    long ResultId,
    int SwimmerId,
    string StyleName,
    string Distance,
    string Gender,
    int? TimeMilliseconds,
    DateTime CompetitionDate,
    bool IsRelay,
    bool TimeFail,
    string? AgeGroup);

/// <summary>Вердикт по одной строке: код причины + человекочитаемое пояснение.</summary>
public sealed record SuspectVerdict(long ResultId, string Reason, string Note);

/// <summary>
/// Проверки достоверности результатов соревнования. Ищут ошибки САМОГО источника — то,
/// что нельзя починить парсером: протокол напечатан так, как напечатан. Живой пример —
/// протокол Маккабиады 2026, где у Elisa MOSHKOVITCH на 100 м баттерфляем стоит 00:32.59
/// (её же полтинник — 27.27), и организаторы посчитали из этого времени 4702 очка. Такой
/// заплыв «бьёт» национальный рекорд, хотя данные приехали к нам корректно.
///
/// Чистая функция: I/O (загрузка строк, запись пометок) — на репозитории.
/// Одна строка получает ОДНУ причину — первую сработавшую в порядке убывания надёжности
/// правила, чтобы пометка отвечала «почему» однозначно.
/// </summary>
public static class SuspectResultDetector
{
    /// <summary>
    /// Мировые рекорды на 2026 — нижняя граница физически возможного. Ключ —
    /// "пол|стиль|дистанция". Пол обязателен: женские рекорды на 5–8% медленнее мужских,
    /// и по мужскому порогу женские ошибки проходят незамеченными (реальный случай —
    /// 00:53.42 на 100 м баттерфляем у Ophir RAKAH: быстрее женского рекорда 54.60,
    /// но медленнее мужского 47.78).
    ///
    /// Правило грубое и потому надёжное: ловит не «слишком быстро для этого пловца», а
    /// «быстрее, чем плавал кто-либо на планете» — возраст и уровень тут не нужны.
    /// </summary>
    private static readonly Dictionary<string, int> WorldBestMs = new()
    {
        ["male|freestyle|50"] = 20_910,       ["female|freestyle|50"] = 23_610,
        ["male|freestyle|100"] = 46_400,      ["female|freestyle|100"] = 51_710,
        ["male|freestyle|200"] = 102_000,     ["female|freestyle|200"] = 112_230,
        ["male|freestyle|400"] = 220_070,     ["female|freestyle|400"] = 235_380,
        ["male|freestyle|800"] = 452_270,     ["female|freestyle|800"] = 484_790,
        ["male|freestyle|1500"] = 866_700,    ["female|freestyle|1500"] = 920_480,
        ["male|backstroke|50"] = 23_550,      ["female|backstroke|50"] = 26_860,
        ["male|backstroke|100"] = 51_600,     ["female|backstroke|100"] = 57_130,
        ["male|backstroke|200"] = 111_920,    ["female|backstroke|200"] = 123_140,
        ["male|breaststroke|50"] = 25_950,    ["female|breaststroke|50"] = 28_370,
        ["male|breaststroke|100"] = 56_880,   ["female|breaststroke|100"] = 64_130,
        ["male|breaststroke|200"] = 125_480,  ["female|breaststroke|200"] = 137_550,
        ["male|butterfly|50"] = 21_320,       ["female|butterfly|50"] = 24_430,
        ["male|butterfly|100"] = 47_780,      ["female|butterfly|100"] = 54_600,
        ["male|butterfly|200"] = 110_340,     ["female|butterfly|200"] = 121_810,
        ["male|individual_medley|100"] = 49_280,  ["female|individual_medley|100"] = 56_510,
        ["male|individual_medley|200"] = 113_700, ["female|individual_medley|200"] = 126_120,
        ["male|individual_medley|400"] = 243_280, ["female|individual_medley|400"] = 264_380,
    };

    /// <summary>
    /// Запаса нет: помечаем всё быстрее мирового рекорда. Настоящий новый мировой рекорд на
    /// израильском протоколе — событие раз в годы, и пометка с него снимается одной кнопкой;
    /// пропущенная же ошибка молча «бьёт» национальные рекорды. Запас в 5% как раз и
    /// прятал 00:53.42 выше.
    /// </summary>
    private const double WorldBestTolerance = 1.0;

    /// <summary>Во сколько раз время должно выбиваться из медианы заплыва.</summary>
    private const double OutlierFactor = 0.6;

    /// <summary>Минимум строк в заплыве, чтобы медиана вообще что-то значила.</summary>
    private const int MinRowsForMedian = 4;

    public static List<SuspectVerdict> Detect(IReadOnlyCollection<SuspectCandidateRow> rows)
    {
        var verdicts = new Dictionary<long, SuspectVerdict>();

        // Эстафеты вне скоупа: у них время команды, а состав и ноги считаются иначе.
        var timed = rows
            .Where(r => !r.IsRelay && !r.TimeFail && r.TimeMilliseconds is > 0)
            .ToList();
        if (timed.Count == 0) return [];

        void Flag(SuspectCandidateRow row, string reason, string note)
        {
            // Первое сработавшее правило побеждает — порядок вызовов ниже и есть приоритет.
            if (!verdicts.ContainsKey(row.ResultId))
                verdicts[row.ResultId] = new SuspectVerdict(row.ResultId, reason, note);
        }

        // 1. Быстрее мирового рекорда — самое надёжное правило, идёт первым.
        foreach (var row in timed)
        {
            if (!TryWorldBest(row.Gender, row.StyleName, TrimDistance(row.Distance), out var best))
                continue;
            if (row.TimeMilliseconds!.Value >= best * WorldBestTolerance) continue;
            Flag(row, SuspectReasons.TimeVsDistance,
                $"{Fmt(row.TimeMilliseconds.Value)} на {row.Distance} м {row.StyleName} — быстрее мирового рекорда ({Fmt(best)})");
        }

        // 2. Время уровня одной ноги эстафеты: быстрее, чем возможно на этой дистанции, но
        //    правдоподобно для вдвое короткой — типичный след того, что в протокол попало
        //    время отрезка, а не заплыва.
        foreach (var row in timed)
        {
            var dist = TrimDistance(row.Distance);
            if (!int.TryParse(dist, out var meters) || meters < 100) continue;
            if (!TryWorldBest(row.Gender, row.StyleName, meters.ToString(), out var best)) continue;
            if (!TryWorldBest(row.Gender, row.StyleName, (meters / 2).ToString(), out var half)) continue;
            var ms = row.TimeMilliseconds!.Value;
            if (ms >= best * WorldBestTolerance) continue;
            if (ms < half * WorldBestTolerance) continue;
            Flag(row, SuspectReasons.RelayTimeInIndividual,
                $"{Fmt(ms)} на {row.Distance} м — похоже на время отрезка {meters / 2} м, а не всей дистанции");
        }

        // 3. Выброс относительно своей дисциплины В СВОЕЙ возрастной ступени.
        //    AgeGroup в ключе обязателен: на детской лиге в одной дисциплине плывут и
        //    восьмилетки (медиана ~59 с), и семнадцатилетние (~30 с). Без ступени медиану
        //    задают младшие, и победители старшей группы становятся «выбросами» — 2026-08-02
        //    так пометились 25.25 и 26.03 на 50 вольным при медиане 43.53 по всем 93 строкам.
        //    Ошибки протокола, ради которых правило и живёт, выбиваются и внутри ступени.
        foreach (var grp in timed.GroupBy(r => (r.StyleName, r.Distance, r.Gender, r.AgeGroup)))
        {
            if (grp.Count() < MinRowsForMedian) continue;
            var sorted = grp.Select(r => r.TimeMilliseconds!.Value).OrderBy(x => x).ToList();
            var median = sorted[sorted.Count / 2];
            foreach (var row in grp)
            {
                if (row.TimeMilliseconds!.Value >= median * OutlierFactor) continue;
                var scope = string.IsNullOrWhiteSpace(row.AgeGroup) ? "дисциплины" : $"ступени {row.AgeGroup}";
                Flag(row, SuspectReasons.TimeOutlier,
                    $"{Fmt(row.TimeMilliseconds.Value)} против медианы {scope} {Fmt(median)} — быстрее, чем физически правдоподобно");
            }
        }

        // 4. Пол результата расходится с полом пловца по остальным его заплывам.
        foreach (var grp in timed.GroupBy(r => r.SwimmerId))
        {
            var byGender = grp
                .Where(r => r.Gender is "male" or "female")
                .GroupBy(r => r.Gender)
                .ToList();
            if (byGender.Count < 2) continue;
            var dominant = byGender.OrderByDescending(g => g.Count()).First();
            foreach (var g in byGender.Where(g => g.Key != dominant.Key))
            foreach (var row in g)
                Flag(row, SuspectReasons.GenderMismatch,
                    $"пол '{row.Gender}', тогда как в остальных заплывах пловца — '{dominant.Key}'");
        }

        // 5. Один пловец дважды в одной дисциплине одного дня с разным временем.
        //    Разные ДНИ — норма (предварительные/финал, повтор дисциплины), поэтому день в ключе.
        foreach (var grp in timed.GroupBy(r =>
                     (r.SwimmerId, r.StyleName, r.Distance, r.CompetitionDate.Date)))
        {
            if (grp.Count() < 2) continue;
            if (grp.Select(r => r.TimeMilliseconds).Distinct().Count() < 2) continue;
            var times = string.Join(", ", grp.Select(r => Fmt(r.TimeMilliseconds!.Value)));
            foreach (var row in grp)
                Flag(row, SuspectReasons.DuplicateSwim,
                    $"дисциплина повторяется в один день с разным временем: {times}");
        }

        return verdicts.Values.OrderBy(v => v.ResultId).ToList();
    }

    /// <summary>
    /// Порог по полу. Для смешанных/неизвестных ("none", mix) берём мужской — он быстрее,
    /// то есть требование мягче: лучше пропустить, чем пометить корректную строку с
    /// неопределённым полом.
    /// </summary>
    private static bool TryWorldBest(string gender, string style, string distance, out int best)
    {
        var key = gender is "male" or "female" ? gender : "male";
        return WorldBestMs.TryGetValue($"{key}|{style}|{distance}", out best);
    }

    /// <summary>Results.Distance приходит и как "100", и как "100m".</summary>
    private static string TrimDistance(string distance)
        => distance.EndsWith('m') ? distance[..^1] : distance;

    private static string Fmt(int ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalMinutes >= 1
            ? $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}"
            : $"{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}";
    }
}
