using System;
using System.Collections.Generic;
using System.Linq;

namespace Swimm.Application.Mapping;

/// <summary>Строка соревнования с эталоном официальных очков.</summary>
/// <param name="SectionKey">Секция протокола, внутри которой организатор раздаёт очки:
/// дисциплина × пол × возрастная полоса × раунд.</param>
/// <param name="Suppressed">Место не зачётное по нашим правилам (предварительный заплыв Р34,
/// общий финал «כללי» Р43) — такая строка у нас не приносит очков независимо от места.</param>
public sealed record OfficialPointsRow(
    long ResultId,
    string SectionKey,
    string Label,
    int? Position,
    int Heat,
    int? TimeMs,
    bool Suppressed,
    int Ours,
    int Official);

/// <summary>Одна причина расхождения: сколько строк и на сколько очков.</summary>
public sealed record OfficialPointsDiffGroup(
    string Kind,
    string Explanation,
    int Rows,
    int Diff,
    IReadOnlyList<string> Examples);

/// <summary>Разбор расхождения целиком.</summary>
public sealed record OfficialPointsDiff(
    int Rows,
    int Ours,
    int Official,
    int Mismatched,
    IReadOnlyList<OfficialPointsDiffGroup> Groups);

/// <summary>
/// Почему наш зачёт не сошёлся с официальным ПОСТРОЧНО (docs/data-integrity.md §10).
///
/// Агрегат «наши X, официальные Y» показывает только величину долга; чинить по нему нечего.
/// Этот разбор отвечает на вопрос «из чего он состоит» и отделяет наши ошибки от дефектов
/// источника. На 1581 он свёл «217 строк необъяснимого шума» к трём внятным причинам,
/// главная из которых — <c>heat-order</c>.
///
/// <c>heat-order</c>: в ВЕЧЕРНИХ финалах официальные очки совпадают не с местом, а с рангом
/// в порядке «номер заплыва, потом время». Заплывы финала стартуют от слабейшего к
/// сильнейшему (C, B, A), и система организатора раздаёт очки за 1-2-3 места участникам
/// первого по счёту заплыва. Внутри события сумма от этого не меняется — между клубами
/// меняется всё. Утренние прямые финалы того же протокола считаются правильно, по времени.
///
/// Воспроизводить этот порядок мы НЕ будем: это чужой дефект, а не регламент. Разбор нужен,
/// чтобы отметка «★ расхождение» на соревновании была доказанной, а не декларативной.
/// </summary>
public static class OfficialPointsDiffAnalyzer
{
    public const string HeatOrder = "heat-order";
    public const string PaidPrelim = "paid-prelim";
    public const string UnpaidSwim = "unpaid-swim";
    public const string Unexplained = "unexplained";

    /// <param name="pointsForPlace">Очки за место по правилу соревнования (0 — вне шкалы).</param>
    public static OfficialPointsDiff Analyze(
        IReadOnlyList<OfficialPointsRow> rows, Func<int?, int> pointsForPlace)
    {
        var heatRank = BuildHeatRanks(rows);
        var groups = new Dictionary<string, (int Rows, int Diff, List<string> Examples)>();

        foreach (var row in rows.Where(r => r.Ours != r.Official))
        {
            var kind =
                // Порядок веток важен: строку, которую мы гасим правилом, объясняет именно
                // это, а не случайное совпадение её официальных очков с рангом по заплывам.
                row.Suppressed && row.Official > 0 ? PaidPrelim
                : row.Ours > 0 && row.Official == 0 ? UnpaidSwim
                : heatRank.TryGetValue(row.ResultId, out var rank)
                  && row.Official == pointsForPlace(rank) ? HeatOrder
                : Unexplained;

            var entry = groups.TryGetValue(kind, out var existing) ? existing : (0, 0, []);
            if (entry.Examples.Count < 5)
                entry.Examples.Add(
                    $"{row.Label}: место {row.Position?.ToString() ?? "—"}, заплыв {row.Heat} — " +
                    $"наши {row.Ours}, официально {row.Official}");
            groups[kind] = (entry.Rows + 1, entry.Diff + row.Ours - row.Official, entry.Examples);
        }

        return new OfficialPointsDiff(
            rows.Count, rows.Sum(r => r.Ours), rows.Sum(r => r.Official),
            groups.Sum(g => g.Value.Rows),
            groups
                .OrderByDescending(g => Math.Abs(g.Value.Diff))
                .ThenByDescending(g => g.Value.Rows)
                .Select(g => new OfficialPointsDiffGroup(
                    g.Key, ExplanationOf(g.Key), g.Value.Rows, g.Value.Diff, g.Value.Examples))
                .ToList());
    }

    /// <summary>
    /// Ранг строки в порядке «номер заплыва, потом время» внутри секции. Равные времена
    /// делят ранг, следующий его пропускает — как места в протоколе. Строки без времени
    /// не ранжируются вовсе: места они не занимают.
    /// </summary>
    private static Dictionary<long, int> BuildHeatRanks(IReadOnlyList<OfficialPointsRow> rows)
    {
        var ranks = new Dictionary<long, int>();
        foreach (var section in rows.Where(r => r.TimeMs is not null).GroupBy(r => r.SectionKey))
        {
            var ordered = section.OrderBy(r => r.Heat).ThenBy(r => r.TimeMs!.Value).ToList();
            var previous = (Heat: int.MinValue, Time: int.MinValue);
            var rank = 0;
            for (var i = 0; i < ordered.Count; i++)
            {
                var current = (ordered[i].Heat, ordered[i].TimeMs!.Value);
                if (current != previous) rank = i + 1;
                previous = current;
                ranks[ordered[i].ResultId] = rank;
            }
        }
        return ranks;
    }

    private static string ExplanationOf(string kind) => kind switch
    {
        HeatOrder =>
            "вечерний финал: официальные очки раздал по рангу «номер заплыва, потом время» " +
            "вместо места — заплывы стартуют от слабейшего, и очки за 1-3 места ушли в первый",
        PaidPrelim =>
            "организатор заплатил за заплыв, который у нас не приносит очков " +
            "(предварительный при наличии финала или общий финал «כללי»)",
        UnpaidSwim =>
            "организатор не заплатил за заплыв, который по регламенту награждается " +
            "(единственный заплыв дисциплины у полосы)",
        _ => "не объясняется ни одной из известных причин — разбирать вручную"
    };
}
