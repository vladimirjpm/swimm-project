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
/// Отрезок = меньшее из двух значений колонки (нарастающее всегда больше, кроме первой
/// колонки, где значение одно). Не сошлась сумма отрезков с итогом — пловец пропускается:
/// лучше без промежуточных, чем с чужими.
/// </summary>
public static class LogligIndividualSplitParser
{
    private static readonly Regex TimeRx = new(@"^\d{1,2}:\d{2}\.\d{2}$", RegexOptions.Compiled);
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
        var columns = new List<(int Dist, double X)>();

        foreach (var page in doc.GetPages())
        {
            var rows = page.GetWords()
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 2.0))
                .OrderByDescending(g => g.Key)
                .Select(g => g.OrderBy(w => w.BoundingBox.Left).ToList())
                .ToList();

            // Шапка колонок: пары «'מ» + число. На странице своя (или повтор прежней).
            var header = rows.FirstOrDefault(r => r.Any(w => w.Text == "'מ") && r.Count(w => DistRx.IsMatch(w.Text)) >= 1
                                                  && !r.Any(w => TimeRx.IsMatch(w.Text)));
            if (header != null)
                columns = header.Where(w => DistRx.IsMatch(w.Text))
                    .Select(w => (int.Parse(w.Text, CultureInfo.InvariantCulture), (double)w.BoundingBox.Left))
                    .OrderBy(c => c.Item1).ToList();
            if (columns.Count < 2) continue; // одна колонка (50 м) — отрезков нет

            (string Time, int Year)? current = null;
            var marks = new Dictionary<int, List<string>>();

            void Flush()
            {
                if (current is { } c && TryLaps(c.Time, marks, columns, out var laps))
                    swims.Add(new SplitSwim(c.Time, c.Year, laps));
                marks = new Dictionary<int, List<string>>();
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

                if (current == null) continue;
                foreach (var w in row.Where(w => TimeRx.IsMatch(w.Text)))
                {
                    // Время печатается левее своего числа в шапке (383 против 396): ближайшая колонка.
                    var dist = columns.OrderBy(c => Math.Abs(c.X - w.BoundingBox.Left)).First().Dist;
                    if (!marks.TryGetValue(dist, out var list)) marks[dist] = list = new List<string>();
                    list.Add(w.Text);
                }
            }
            Flush();
        }

        return swims;
    }

    private static bool TryLaps(string total, Dictionary<int, List<string>> marks,
        List<(int Dist, double X)> columns, out List<string> laps)
    {
        laps = new List<string>();
        foreach (var (dist, _) in columns)
        {
            if (!marks.TryGetValue(dist, out var values) || values.Count == 0) return false;
            laps.Add(values.OrderBy(ToMs).First());
        }

        // Страховка: сумма отрезков обязана дать итог (±0.02 на округление протокола).
        var sum = laps.Sum(ToMs);
        if (Math.Abs(sum - ToMs(total)) > 20) return false;

        laps = laps.Select(Short).ToList();
        return true;
    }

    /// <summary>«00:34.84» → «34.84», «01:08.68» → «1:08.68» — как у существующих time_split.</summary>
    private static string Short(string t)
    {
        var s = t.StartsWith("00:", StringComparison.Ordinal) ? t[3..] : t;
        return s.StartsWith('0') && s.Contains(':') ? s[1..] : s;
    }

    private static long ToMs(string time)
    {
        double seconds = 0;
        foreach (var p in time.Split(':'))
            seconds = seconds * 60 + double.Parse(p, CultureInfo.InvariantCulture);
        return (long)Math.Round(seconds * 1000);
    }
}
