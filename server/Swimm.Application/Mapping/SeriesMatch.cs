namespace Swimm.Application.Mapping;

/// <summary>
/// Узнавание выпуска серии турнира (docs/plans/official-awards-plan.md §5.8) — правило одно на
/// импорт (<c>SeriesBinder</c>), «Входящие», проверки Health и подсказку в /Admin/Series.
///
/// Серия — по ключевой фразе: нормализованное название (<see cref="DiscoveryNameMatch.Normalize"/>)
/// СОДЕРЖИТ фразу. Не похожесть названий: у выпусков 2025 и 2026 разные начала («תחרות הנצחה…» /
/// «תחרות השחייה הארצית…»), держатся имена, в честь которых турнир. Дата не проверяется — турнир
/// пропускал сезон.
///
/// Часть — по возрастам пловцов дня, а не по номеру дня и не по SubName (он одинаков у обоих дней).
/// </summary>
public static class SeriesMatch
{
    /// <summary>
    /// Доля пловцов дня в возрастах части, с которой день уходит в эту часть. Замер 26.10 на
    /// выпусках 2025/2026: «своя» часть — 100%, чужая — 12–17% (пересечение на 14 годах).
    /// </summary>
    public const double PartShareThreshold = 0.8;

    /// <summary>Название подходит под одну из фраз серии.</summary>
    public static bool NameMatches(string? name, IEnumerable<string> phrases)
    {
        var key = DiscoveryNameMatch.Normalize(name);
        return key.Length > 0 && phrases
            .Select(DiscoveryNameMatch.Normalize)
            .Any(p => p.Length > 0 && key.Contains(p, StringComparison.Ordinal));
    }

    /// <summary>Часть серии с возрастами «кто плывёт в этот день».</summary>
    public sealed record PartAges(int PartId, int SortOrder, int? MinSeasonAge, int? MaxSeasonAge);

    /// <summary>Итог выбора части: Id или причина, почему не выбрана.</summary>
    public sealed record PartPick(int? PartId, double Share, string Reason);

    /// <summary>
    /// Часть для дня по сезонным возрастам его пловцов (по одному на пловца). Одна часть — она и
    /// есть. Иначе — часть с наибольшей долей, если она ≥ <see cref="PartShareThreshold"/> и строго
    /// выше второй; ничья — по <c>dayNumber</c> ↔ SortOrder, иначе не выбрано.
    /// </summary>
    public static PartPick PickPart(IReadOnlyList<PartAges> parts, IReadOnlyCollection<int> swimmerAges, int? dayNumber)
    {
        if (parts.Count == 0) return new(null, 0, "у серии нет частей");
        if (parts.Count == 1) return new(parts[0].PartId, 1, "единственная часть");
        if (swimmerAges.Count == 0) return new(null, 0, "у дня нет пловцов с годом рождения");

        var shares = parts
            .Select(p => (Part: p, Share: swimmerAges.Count(a =>
                (p.MinSeasonAge is not int min || a >= min) && (p.MaxSeasonAge is not int max || a <= max))
                / (double)swimmerAges.Count))
            .OrderByDescending(x => x.Share)
            .ToList();

        var best = shares[0];
        if (best.Share < PartShareThreshold)
            return new(null, best.Share, $"ни одна часть не набирает {PartShareThreshold:P0} пловцов дня (лучшая — {best.Share:P0})");

        var tied = shares.Where(x => x.Share == best.Share).ToList();
        if (tied.Count == 1) return new(best.Part.PartId, best.Share, $"{best.Share:P0} пловцов дня в возрастах части");

        var byDay = tied.Where(x => dayNumber is int n && x.Part.SortOrder == n).ToList();
        return byDay.Count == 1
            ? new(byDay[0].Part.PartId, best.Share, $"{best.Share:P0} у нескольких частей, выбрана по номеру дня")
            : new(null, best.Share, $"{best.Share:P0} у нескольких частей — неоднозначно");
    }

    /// <summary>
    /// Общая часть названий выпусков — самая длинная последовательность слов, которая есть в
    /// каждом нормализованном названии (подсказка для MatchPhrases). null — названий меньше двух
    /// или общей части короче двух слов.
    /// </summary>
    public static string? CommonPhrase(IEnumerable<string?> names)
    {
        var keys = names.Select(DiscoveryNameMatch.Normalize).Where(k => k.Length > 0).Distinct().ToList();
        if (keys.Count < 2) return null;

        var words = keys[0].Split(' ');
        for (var len = words.Length; len >= 2; len--)
            for (var start = 0; start + len <= words.Length; start++)
            {
                var candidate = string.Join(' ', words, start, len);
                if (keys.Skip(1).All(k => (" " + k + " ").Contains(" " + candidate + " ", StringComparison.Ordinal)))
                    return candidate;
            }
        return null;
    }
}
