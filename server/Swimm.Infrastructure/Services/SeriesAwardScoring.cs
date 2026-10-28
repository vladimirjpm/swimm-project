using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Services;

/// <summary>Личный старт в объёме, нужном для официальной награды серии.</summary>
/// <param name="SeasonAge">Сезонный возраст (<c>SeasonMath.AgeInSeason</c>); null — года рождения нет.</param>
public sealed record SeriesAwardRow(int SwimmerId, string Gender, int? SeasonAge, int Points);

/// <summary>Призёр группы.</summary>
/// <param name="Parts">Очки стартов, вошедших в сумму, по убыванию («674+650+598»).</param>
public sealed record SeriesAwardPlace(
    int Place, int SwimmerId, string Gender, int Points, IReadOnlyList<int> Parts, string? Prize, bool IsTie);

/// <summary>Группа награды: полоса регламента × пол. Gender null — оба пола вместе.</summary>
public sealed record SeriesAwardGroup(
    string? Gender, int? MinSeasonAge, int? MaxSeasonAge, IReadOnlyList<SeriesAwardPlace> Places);

/// <summary>
/// Чистый расчёт официальной награды серии (<see cref="SeriesAward"/>, docs/plans/official-awards-plan.md
/// §5.3) — по строкам ОДНОГО дня. Отбор строк (личные, без TimeFail и SuspectReason, очки FINA &gt; 0)
/// делает вызывающий; здесь — группы, сумма лучших и места.
///
/// Отличия от High Point (<see cref="PointRulesSwimmersScoring"/>): группы — полосы регламента по
/// СЕЗОННОМУ возрасту, а не возраст на год соревнования и не группы протокола; призёров N, а не
/// один; места — по <c>rank()</c>: двое на 2-м месте — следующий 4-й.
/// </summary>
public static class SeriesAwardScoring
{
    public static List<SeriesAwardGroup> Compute(SeriesAward award, IReadOnlyCollection<SeriesAwardRow> rows)
    {
        if (award.Kind != SeriesAwardKinds.PointsSum || award.Places < 1) return [];

        return GroupsOf(award)
            .Select(g => new SeriesAwardGroup(g.Gender, g.Min, g.Max,
                Rank(award, rows.Where(r => g.Contains(r)).ToList())))
            .ToList();
    }

    private sealed record Group(string? Gender, int? Min, int? Max, bool AgeBound)
    {
        public bool Contains(SeriesAwardRow r) =>
            (Gender is null || r.Gender == Gender)
            && (!AgeBound || (r.SeasonAge is int age
                              && (Min is not int min || age >= min)
                              && (Max is not int max || age <= max)));
    }

    private static readonly string[] Genders = ["male", "female"];

    /// <summary>
    /// Нет полос — все возраста вместе (деньги 23.10), по полу, если SplitByGender. Полоса без пола
    /// при SplitByGender делится на две. Порядок — по возрасту, внутри — ♂ затем ♀.
    /// </summary>
    private static IEnumerable<Group> GroupsOf(SeriesAward award)
    {
        if (award.Bands.Count == 0)
            return award.SplitByGender
                ? Genders.Select(g => new Group(g, null, null, AgeBound: false))
                : [new Group(null, null, null, AgeBound: false)];

        return award.Bands
            .SelectMany(b => b.Gender is null && award.SplitByGender
                ? Genders.Select(g => new Group(g, b.MinSeasonAge, b.MaxSeasonAge, AgeBound: true))
                : [new Group(b.Gender, b.MinSeasonAge, b.MaxSeasonAge, AgeBound: true)])
            .OrderBy(g => g.Min ?? 0)
            .ThenBy(g => g.Max ?? int.MaxValue)
            .ThenBy(g => g.Gender == "female" ? 1 : 0);
    }

    private static List<SeriesAwardPlace> Rank(SeriesAward award, List<SeriesAwardRow> rows)
    {
        var perSwimmer = rows
            .GroupBy(r => r.SwimmerId)
            .Select(g =>
            {
                var parts = g.Select(r => r.Points).OrderByDescending(p => p).AsEnumerable();
                if (award.CountBest is int take) parts = parts.Take(take);
                var list = parts.ToList();
                return (SwimmerId: g.Key, g.First().Gender, Points: list.Sum(), Parts: list);
            })
            .ToList();

        var prizes = award.Prizes.ToDictionary(p => p.Place, p => p.Text);
        return perSwimmer
            .Select(s => (s, Place: 1 + perSwimmer.Count(o => o.Points > s.Points)))
            .Where(x => x.Place <= award.Places)
            .Select(x => new SeriesAwardPlace(
                x.Place, x.s.SwimmerId, x.s.Gender, x.s.Points, x.s.Parts,
                prizes.GetValueOrDefault(x.Place),
                IsTie: perSwimmer.Count(o => o.Points == x.s.Points) > 1))
            .OrderBy(p => p.Place)
            .ThenBy(p => p.SwimmerId)
            .ToList();
    }
}
