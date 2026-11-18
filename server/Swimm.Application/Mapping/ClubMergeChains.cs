namespace Swimm.Application.Mapping;

/// <summary>
/// Цепочки склеек клубов (docs/plans/swimmer-club-change-plan.md §5, Н4). Склеенный клуб
/// (<c>Club.MergedIntoId</c>) обязан указывать прямо на ЖИВОЙ клуб: страница
/// <c>/clubs/{id}</c>, импорт и полтора десятка выборок делают один шаг
/// <c>MergedIntoId ?? Id</c>. Цепочка <c>1329 → 605 → 1372</c> открывала пустой 605.
///
/// Чистые функции: справочник «клуб → в кого склеен» приносит вызывающий.
/// </summary>
public static class ClubMergeChains
{
    /// <summary>Сколько звеньев проходить: длиннее — петля или ошибка данных.</summary>
    public const int MaxHops = 16;

    /// <summary>
    /// Итоговый живой клуб для <paramref name="clubId"/>: идёт по <c>MergedIntoId</c> до клуба,
    /// который никуда не склеен. null — петля, слишком длинная цепочка или клуба нет в справочнике.
    /// Живой клуб возвращает сам себя.
    /// </summary>
    public static int? FinalOf(int clubId, IReadOnlyDictionary<int, int?> mergedInto)
    {
        var current = clubId;
        var seen = new HashSet<int>();
        for (var hop = 0; hop <= MaxHops; hop++)
        {
            if (!mergedInto.TryGetValue(current, out var next)) return null;
            if (next is not int target) return current;
            if (!seen.Add(current)) return null;
            current = target;
        }
        return null;
    }

    /// <summary>
    /// План догона: какие склеенные клубы смотрят не на живой клуб и куда их перевесить.
    /// <c>Broken</c> — надгробия, у которых конца цепочки нет (петля, обрыв): их чинит человек.
    /// </summary>
    public static ClubChainPlan Plan(IReadOnlyDictionary<int, int?> mergedInto)
    {
        var moves = new List<ClubChainMove>();
        var broken = new List<int>();
        foreach (var (id, target) in mergedInto.OrderBy(kv => kv.Key))
        {
            if (target is not int direct) continue;
            var final = FinalOf(id, mergedInto);
            if (final is not int end) { broken.Add(id); continue; }
            if (end != direct) moves.Add(new ClubChainMove(id, direct, end));
        }
        return new ClubChainPlan(moves, broken);
    }
}

/// <summary>Склеенный клуб <paramref name="ClubId"/> смотрел на <paramref name="From"/>, теперь — на <paramref name="To"/>.</summary>
public sealed record ClubChainMove(int ClubId, int From, int To);

/// <summary>План догона цепочек склеек.</summary>
public sealed record ClubChainPlan(IReadOnlyList<ClubChainMove> Moves, IReadOnlyList<int> Broken);
