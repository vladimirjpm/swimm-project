using System;
using System.Collections.Generic;
using System.Linq;

namespace Swimm.Application.Mapping;

/// <summary>Личная строка пловца для правила текущего клуба: клуб строки и её соревнование.</summary>
/// <param name="ClubId">Клуб строки (<c>Results.ClubId</c>), как напечатан в протоколе.</param>
/// <param name="CompetitionId">День (<c>Results.CompetitionId</c>).</param>
/// <param name="EventId">Событие многодневки (<c>Competitions.EventId</c>); null — однодневка.</param>
/// <param name="Date">Дата строки (<c>Results.CompetitionDate</c>).</param>
/// <param name="Count">Сколько таких строк (запрос может отдать их уже сгруппированными).</param>
public sealed record SwimmerClubRow(int ClubId, int CompetitionId, int? EventId, DateTime Date, int Count = 1);

/// <summary>Что правилу нужно знать о клубе: псевдо ли он и куда склеен.</summary>
public sealed record ClubFacts(bool IsPseudo, int? MergedIntoId);

/// <summary>Период выступлений за клуб: первая и последняя дата строк.</summary>
public sealed record SwimmerClubPeriod(int ClubId, DateTime First, DateTime Last, int Rows);

/// <summary>
/// «Текущий клуб» пловца — то, что лежит в <c>Swimmers.ClubId</c> (решение К2 плана
/// docs/plans/swimmer-club-change-plan.md, Р94 в docs/data-integrity.md).
///
/// Правило: клуб ПОСЛЕДНЕГО соревнования пловца по дате.
/// • Соревнование — событие многодневки целиком (<c>EventId</c>), однодневка — свой день. Дата
///   соревнования — последняя дата его строк. Два разных соревнования в одну дату считаются вместе.
/// • Псевдоклубы (страна/сборная, <c>Club.IsPseudo</c>) не клуб пловца — их строки не в счёт.
/// • Склеенный клуб (<c>Club.MergedIntoId</c>) считается своим приёмником (по цепочке).
/// • В последнем соревновании два клуба — берём тот, где строк больше. Поровну — не меняем:
///   правды в данных нет, а прыгать туда-сюда при каждом импорте хуже, чем оставить как есть.
/// • Закреплённый клуб (<c>Swimmers.ClubLocked</c>, решение К3) правило не трогает.
///
/// Строки — только ЛИЧНЫЕ: строка эстафеты принадлежит первой ноге (<c>Results.SwimmerId</c>), у
/// остальных ног её нет, и счёт «где строк больше» поехал бы у одного пловца из четырёх.
///
/// Ответ — новый клуб или null («не менять»). Повторный прогон на тех же данных ничего не меняет.
/// </summary>
public static class SwimmerCurrentClubRule
{
    /// <summary>Сколько шагов по цепочке склеек проходим: защита от петли в данных.</summary>
    private const int MaxMergeHops = 10;

    /// <summary>
    /// Новый клуб пловца или null — менять не надо (закреплён, нет строк с настоящим клубом,
    /// ничья в последнем соревновании, клуб и так верный).
    /// </summary>
    public static int? Decide(
        int? currentClubId, bool locked,
        IEnumerable<SwimmerClubRow> rows, IReadOnlyDictionary<int, ClubFacts> clubs)
    {
        if (locked) return null;
        var wanted = CurrentClub(rows, clubs);
        return wanted is int club && club != currentClubId ? club : null;
    }

    /// <summary>Клуб последнего соревнования без учёта карточки; null — не определить.</summary>
    public static int? CurrentClub(IEnumerable<SwimmerClubRow> rows, IReadOnlyDictionary<int, ClubFacts> clubs)
    {
        var real = RealRows(rows, clubs).ToList();
        if (real.Count == 0) return null;

        var competitions = real
            .GroupBy(r => r.Row.EventId is int e ? $"e{e}" : $"c{r.Row.CompetitionId}")
            .Select(g => new { Date = g.Max(r => r.Row.Date.Date), Rows = g.ToList() })
            .ToList();
        var lastDate = competitions.Max(c => c.Date);

        var counts = competitions
            .Where(c => c.Date == lastDate)
            .SelectMany(c => c.Rows)
            .GroupBy(r => r.ClubId)
            .Select(g => new { ClubId = g.Key, Count = g.Sum(r => r.Row.Count) })
            .OrderByDescending(x => x.Count)
            .ToList();

        if (counts.Count > 1 && counts[0].Count == counts[1].Count) return null; // ничья
        return counts[0].ClubId;
    }

    /// <summary>
    /// Периоды выступлений за каждый настоящий клуб (склеенные — приёмником), по дате первой строки.
    /// Для отчёта догона: пересекающиеся периоды — пловец в двух клубах одновременно.
    /// </summary>
    public static IReadOnlyList<SwimmerClubPeriod> Periods(
        IEnumerable<SwimmerClubRow> rows, IReadOnlyDictionary<int, ClubFacts> clubs) =>
        RealRows(rows, clubs)
            .GroupBy(r => r.ClubId)
            .Select(g => new SwimmerClubPeriod(g.Key, g.Min(r => r.Row.Date.Date), g.Max(r => r.Row.Date.Date), g.Sum(r => r.Row.Count)))
            .OrderBy(p => p.First).ThenBy(p => p.ClubId)
            .ToList();

    /// <summary>Пересекаются ли периоды хотя бы двух клубов (общий день тоже пересечение).</summary>
    public static bool HasOverlap(IReadOnlyList<SwimmerClubPeriod> periods)
    {
        for (var i = 0; i < periods.Count; i++)
            for (var j = i + 1; j < periods.Count; j++)
                if (periods[i].First <= periods[j].Last && periods[j].First <= periods[i].Last)
                    return true;
        return false;
    }

    /// <summary>Клуб с приёмником склейки вместо склеенного; null — псевдоклуб или клуба нет.</summary>
    public static int? Resolve(int clubId, IReadOnlyDictionary<int, ClubFacts> clubs)
    {
        var id = clubId;
        for (var hop = 0; hop <= MaxMergeHops; hop++)
        {
            if (!clubs.TryGetValue(id, out var facts)) return null;
            if (facts.MergedIntoId is not int next || next == id)
                return facts.IsPseudo ? null : id;
            id = next;
        }
        return null; // петля склеек — не гадаем
    }

    private static IEnumerable<(int ClubId, SwimmerClubRow Row)> RealRows(
        IEnumerable<SwimmerClubRow> rows, IReadOnlyDictionary<int, ClubFacts> clubs)
    {
        foreach (var row in rows)
            if (Resolve(row.ClubId, clubs) is int club)
                yield return (club, row);
    }
}
