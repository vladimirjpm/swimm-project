using System;
using System.Collections.Generic;
using System.Linq;

namespace Swimm.Application.Mapping;

/// <summary>
/// Сведение строк источника и базы по ключу «день + дисциплина + пол + заплыв + дорожка», когда
/// ключ НЕ уникален (07.11.2026, сверка 237 соревнований).
/// </summary>
/// <remarks>
/// У чемпионатов loglig заводит отдельное событие на каждый возраст («100 חזה - בנות 11», «בנות 12»…)
/// и нумерует заплывы в каждом с 1; у «мокдамот и финал» один пловец плывёт дважды на той же
/// дорожке. Ключ из одних заплыва и дорожки тогда общий у нескольких строк, и прежняя сверка
/// объявляла весь план неприменимым (40 детских соревнований так и не проверились).
/// Внутри общего ключа строки сводятся по ДОКАЗАТЕЛЬСТВУ — времени и году рождения. Остаток
/// «один у источника и один у нас» — пара с расхождением (как у уникального ключа). Всё прочее —
/// неоднозначность: в <see cref="PlacePlan.Problems"/>, запись тогда запрещена.
/// </remarks>
public static partial class LogligPlaceMatcher
{
    private sealed record Pairing(
        List<(PlaceRowFromSource Src, PlaceRowInDb Ours, string Key)> Pairs,
        List<string> Unmatched,
        List<string> Problems);

    private static Pairing PairRows(IReadOnlyList<PlaceRowFromSource> source, IReadOnlyList<PlaceRowInDb> db)
    {
        var srcByKey = source.GroupBy(r => Key(r.Date, r.StyleName, r.Distance, r.Gender, r.Heat, r.Lane))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var dbByKey = db.GroupBy(r => Key(r.Date, r.StyleName, r.Distance, r.Gender, r.Heat, r.Lane))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var result = new Pairing([], [], []);
        foreach (var key in srcByKey.Keys.Union(dbByKey.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var src = srcByKey.GetValueOrDefault(key) ?? [];
            var ours = dbByKey.GetValueOrDefault(key) ?? [];

            if (src.Count > 1 || ours.Count > 1)
                (src, ours) = PairByProof(key, src, ours, result.Pairs);

            if (src.Count == 1 && ours.Count == 1)
            {
                result.Pairs.Add((src[0], ours[0], key));
                continue;
            }
            if (src.Count > 0 && ours.Count > 0)
            {
                result.Problems.Add($"ключ {key}: у источника {src.Count}, у нас {ours.Count} — пары не доказаны");
                continue;
            }
            foreach (var s in src)
                result.Unmatched.Add($"у источника, нет в базе: {key} · {s.FullName} {s.BirthYear} · {Ms(s.TimeMs)} · место {s.Position}");
            foreach (var o in ours)
                result.Unmatched.Add($"в базе, нет у источника: {key} · {o.FullName} {o.BirthYear} · {Ms(o.TimeMs)} · место {o.Position}");
        }
        return result;
    }

    /// <summary>
    /// Сводит строки общего ключа с равными временем и годом рождения (по одной на каждую сторону;
    /// одинаковые доказательства — по порядку, они неразличимы и для места). Возвращает остаток.
    /// </summary>
    private static (List<PlaceRowFromSource> Src, List<PlaceRowInDb> Ours) PairByProof(
        string key, List<PlaceRowFromSource> src, List<PlaceRowInDb> ours,
        List<(PlaceRowFromSource, PlaceRowInDb, string)> pairs)
    {
        var restSrc = new List<PlaceRowFromSource>();
        var freeOurs = new List<PlaceRowInDb>(ours);
        foreach (var s in src)
        {
            var match = freeOurs.FirstOrDefault(o => o.TimeMs == s.TimeMs && SameYear(s.BirthYear, o.BirthYear));
            if (match is null) { restSrc.Add(s); continue; }
            freeOurs.Remove(match);
            pairs.Add((s, match, key));
        }
        return (restSrc, freeOurs);
    }

    private static bool SameYear(int? sourceYear, int ourYear) =>
        sourceYear is null or 0 || ourYear == 0 || sourceYear == ourYear;
}
