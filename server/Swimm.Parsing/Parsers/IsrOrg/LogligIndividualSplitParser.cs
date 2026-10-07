using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Swimm.Parsing.Parsers.IsrOrg;

/// <summary>Пловец из PDF промежуточных личной дисциплины: итог, год и отрезки.</summary>
/// <param name="Time">Итог как в протоколе («01:06.36»).</param>
/// <param name="Laps">Время каждого отрезка от старта к финишу («31.52», «34.84»).</param>
public sealed record SplitSwim(string Time, int BirthYear, IReadOnlyList<string> Laps);

/// <summary>
/// Разбор PDF «זמני ביניים» ЛИЧНОЙ дисциплины loglig (тот же экспорт, что у эстафет —
/// <see cref="LogligRelaySplitParser"/>; docs/relays.md, «Промежуточные эстафет»).
///
/// Вёрстка (живой файл 13805 / 73036 и 73034): строка пловца (очки, реакция, итог в колонке
/// «תוצאה», клуб, год, имя, дорожка/заплыв/место), под ней две строки времён по колонкам
/// дистанций — шапка «'מ 200 · 'מ 150 · 'מ 100 · 'מ 50»:
/// первая — НАРАСТАЮЩЕЕ время на отметке, вторая — время ОТРЕЗКА (у первых 50 м отрезок и
/// есть нарастающее, поэтому во второй строке этой колонки нет). Колонка времени
/// определяется по ближайшему числу шапки, а не по порядку слов.
///
/// Отрезки считаем РАЗНОСТЬЮ нарастающих, а не берём из строки отрезков: так было до
/// 07.10.2026, и на 6595 (loglig 11586) это теряло пловцов дважды:
/// - у 800 м отметок 16, и шапка печатается В ДВЕ СТРОКИ («50…550» и «600…800») — полосы.
///   Времена второй полосы стоят под теми же X, что первой, а отрезок первой колонки
///   полосы (600 м) не печатается вовсе, как у 50 м. Полоса строки определяется так: строка
///   нарастающих — та, где есть время в ПЕРВОЙ колонке следующей полосы; строка отрезков её не
///   имеет никогда;
/// - время бывает с одной цифрой после точки («00:40.0», «05:24.0») — оно не читалось, сумма
///   не сходилась, и пловец пропадал целиком (по 1–9 в дисциплине).
/// Нарастающие обязаны расти, последнее — совпасть с итогом (±0.02 на округление протокола);
/// иначе пловец пропускается: лучше без промежуточных, чем с чужими.
/// </summary>
public static class LogligIndividualSplitParser
{
    private static readonly Regex TimeRx = new(@"^\d{1,2}:\d{2}\.\d{1,2}$", RegexOptions.Compiled);
    private static readonly Regex YearRx = new(@"^(19|20)\d{2}$", RegexOptions.Compiled);
    private static readonly Regex DistRx = new(@"^\d{2,4}$", RegexOptions.Compiled);

    /// <summary>
    /// Колонка итога («תוצאה», x≈156) — левее всех отметок дистанций. Граница нужна только в
    /// строке пловца: отметки стоят в СВОИХ строках (без года), и у 400 м колонки 400/350
    /// уходят левее 250 — там берутся все времена строки.
    /// </summary>
    private const double ResultColumnMaxX = 175;

    public static List<SplitSwim> Parse(Stream pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        var swims = new List<SplitSwim>();
        // Полосы шапки: у 800/1500 м отметки не влезают в строку и переносятся на следующую.
        var bands = new List<List<(int Dist, double X)>>();

        foreach (var page in doc.GetPages())
        {
            var rows = page.GetWords()
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 2.0))
                .OrderByDescending(g => g.Key)
                .Select(g => g.OrderBy(w => w.BoundingBox.Left).ToList())
                .ToList();

            // Шапка колонок: строки пар «'מ» + число. На странице своя (или повтор прежней).
            var headers = rows
                .Where(r => r.Any(w => w.Text == "'מ") && r.Any(w => DistRx.IsMatch(w.Text))
                            && !r.Any(w => TimeRx.IsMatch(w.Text)))
                .Select(r => r.Where(w => DistRx.IsMatch(w.Text))
                    .Select(w => (Dist: int.Parse(w.Text, CultureInfo.InvariantCulture), X: (double)w.BoundingBox.Left))
                    .OrderBy(c => c.Dist).ToList())
                .ToList();
            if (headers.Count > 0)
                bands = headers.DistinctBy(b => b[0].Dist).OrderBy(b => b[0].Dist).ToList();
            if (bands.Sum(b => b.Count) < 2) continue; // одна колонка (50 м) — отрезков нет

            (string Time, int Year)? current = null;
            var cumulative = new Dictionary<int, string>();
            var band = -1;

            void Flush()
            {
                if (current is { } c && TryLaps(c.Time, cumulative, bands, out var laps))
                    swims.Add(new SplitSwim(c.Time, c.Year, laps));
                cumulative = new Dictionary<int, string>();
                band = -1;
            }

            foreach (var row in rows)
            {
                var result = row.FirstOrDefault(w => TimeRx.IsMatch(w.Text) && w.BoundingBox.Left < ResultColumnMaxX);
                var year = row.FirstOrDefault(w => YearRx.IsMatch(w.Text));
                if (result != null && year != null)
                {
                    Flush();
                    current = (result.Text, int.Parse(year.Text, CultureInfo.InvariantCulture));
                    continue;
                }

                var times = row.Where(w => TimeRx.IsMatch(w.Text)).ToList();
                if (current == null || times.Count == 0 || band + 1 >= bands.Count) continue;

                // Строка нарастающих следующей полосы — есть время в её ПЕРВОЙ колонке (отрезок
                // первой колонки не печатается никогда). Иначе это строка отрезков — пропускаем.
                var next = bands[band + 1];
                if (!times.Any(w => Nearest(next, w).Dist == next[0].Dist)) continue;

                band++;
                foreach (var w in times)
                    cumulative[Nearest(next, w).Dist] = w.Text;
            }
            Flush();
        }

        return swims;
    }

    /// <summary>Время печатается левее своего числа в шапке (383 против 396): ближайшая колонка.</summary>
    private static (int Dist, double X) Nearest(List<(int Dist, double X)> columns, Word w) =>
        columns.OrderBy(c => Math.Abs(c.X - w.BoundingBox.Left)).First();

    private static bool TryLaps(string total, Dictionary<int, string> cumulative,
        List<List<(int Dist, double X)>> bands, out List<string> laps)
    {
        laps = new List<string>();
        long previous = 0;
        foreach (var (dist, _) in bands.SelectMany(b => b).OrderBy(c => c.Dist))
        {
            if (!cumulative.TryGetValue(dist, out var mark)) return false;
            var ms = ToMs(mark);
            if (ms <= previous) return false; // нарастающее обязано расти — иначе колонки разъехались
            laps.Add(FormatLap(ms - previous));
            previous = ms;
        }

        // Страховка: последняя отметка — это итог (±0.02 на округление протокола).
        return Math.Abs(previous - ToMs(total)) <= 20;
    }

    /// <summary>34 840 мс → «34.84», 68 680 → «1:08.68» — как у существующих time_split.</summary>
    private static string FormatLap(long ms)
    {
        var minutes = ms / 60000;
        var seconds = (ms % 60000) / 1000.0;
        return minutes > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00.00}", minutes, seconds)
            : seconds.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static long ToMs(string time)
    {
        double seconds = 0;
        foreach (var p in time.Split(':'))
            seconds = seconds * 60 + double.Parse(p, CultureInfo.InvariantCulture);
        return (long)Math.Round(seconds * 1000);
    }
}
