using Microsoft.EntityFrameworkCore;
using Swimm.Domain;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Клубные очки правил «по заплыву целиком» (<see cref="PointRulesClubsScoring.NeedsContext"/>,
/// схема Красного моря) для мест, которые считают очки построчно: зачёт Overview, очки строк
/// протокола, материализованный зачёт, лучшие пловцы клуба.
///
/// Вызывающему мало своих строк: абсолютное место и размер категории зависят от ВСЕХ участников
/// заплыва, а у него бывает страница протокола или один клуб. Поэтому соревнования с таким
/// правилом догружаются целиком — отдельным запросом и только когда такое правило вообще есть
/// (их единицы), — и вызывающий берёт очки строки отсюда по её Id вместо
/// <see cref="PointRulesClubsScoring.RelayPointsFor"/>.
/// </summary>
public static class ContextClubPoints
{
    private static readonly IReadOnlyDictionary<long, int> Empty = new Dictionary<long, int>();

    /// <summary>Id соревнований из набора, чьё правило (привязка важнее подбора) требует контекста.</summary>
    public static IReadOnlyCollection<int> CompetitionsNeedingContext(
        IReadOnlyCollection<PointRuleClubs> rules,
        IEnumerable<(int CompetitionId, int? RuleId, bool IsMasters, DateOnly Date)> competitions)
    {
        if (!rules.Any(PointRulesClubsScoring.NeedsContext)) return [];
        return competitions
            .Where(c => PointRulesClubsScoring.NeedsContext(
                CompetitionRuleResolver.Resolve(rules, c.RuleId, c.IsMasters, c.Date)))
            .Select(c => c.CompetitionId)
            .ToHashSet();
    }

    /// <summary>Очки всех строк указанных соревнований: Id строки → очки.</summary>
    public static async Task<IReadOnlyDictionary<long, int>> LoadAsync(
        IQueryable<ResultRecord> results, IReadOnlyCollection<PointRuleClubs> rules,
        IReadOnlyCollection<int> competitionIds, CancellationToken ct = default)
    {
        if (competitionIds.Count == 0) return Empty;

        var rows = await results.AsNoTracking()
            .Where(r => competitionIds.Contains(r.CompetitionId))
            .Select(r => new
            {
                r.Id,
                r.CompetitionId,
                r.StyleId,
                r.Distance,
                r.Gender,
                r.EventCategory,
                // Тот же фильтр незачётных раундов, что у построчного расчёта (Р34, Р43).
                Position = r.HeatType == "prelim" || r.HeatType == "extra" || r.Round == ResultRounds.FinalOpen
                    ? null : r.Position,
                Awarding = r.HeatType != "prelim" && r.HeatType != "extra",
                r.TimeMillisecond,
                r.TimeFail,
                r.TimeFailNote,
                IsRelay = r.RelayId != null,
                r.Competition.IsMasters,
                RuleId = r.Competition.PointRuleClubsId,
                r.CompetitionDate,
            })
            .ToListAsync(ct);

        var points = new Dictionary<long, int>(rows.Count);
        foreach (var competition in rows.GroupBy(r => r.CompetitionId))
        {
            var head = competition.First();
            var rule = CompetitionRuleResolver.Resolve(
                rules, head.RuleId, head.IsMasters, DateOnly.FromDateTime(head.CompetitionDate));
            if (rule is null || !PointRulesClubsScoring.NeedsContext(rule)) continue;

            var set = PointRulesClubsScoring.PointsForSet(rule, competition.Select(r => new ClubPointsContextRow(
                r.Id, $"{r.StyleId}|{r.Distance}|{r.Gender}", r.Distance, r.EventCategory,
                r.Position, r.TimeMillisecond, r.TimeFail, r.TimeFailNote, r.IsRelay, r.Awarding)));
            foreach (var (id, p) in set) points[id] = p;
        }

        return points;
    }
}
