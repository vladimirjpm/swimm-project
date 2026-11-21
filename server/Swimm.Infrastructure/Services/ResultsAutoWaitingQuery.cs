using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// «Ждёт тебя» у results-auto для мониторинга /Admin/Jobs. Правило — <see cref="ResultsAutoWaiting"/>
/// (то же, что считает блок «Соревнования» дашборда), «уже в базе» — <see cref="DiscoveryCompetitionMatcher"/>.
/// Запросов: один по «Входящим» (вердикт, окно, статус — в SQL) и ещё один по соревнованиям, только
/// если кандидаты нашлись.
/// </summary>
public sealed class ResultsAutoWaitingQuery(SwimmDbContext db) : IResultsAutoWaitingQuery
{
    public async Task<IReadOnlyList<ResultsAutoWaitingRow>> ListAsync(DateTime now, CancellationToken ct = default)
    {
        var since = ResultsAutoWaiting.Since(now);
        // Застрявшие — по дате старта, а не по времени проверки (правило — ResultsAutoWaiting.IsStuck).
        var stuckUntil = now - ResultsAutoWaiting.StuckAfter;
        var stuckFrom = now.AddDays(-(ResultsAutoRules.WindowDays + 1));
        var candidates = await db.DiscoveredCompetitions.AsNoTracking()
            .Where(d => d.Status == DiscoveredCompetitionStatus.New
                        && ((d.AutoPullVerdict == ResultsAutoVerdicts.Blocked && d.AutoPullCheckedAt >= since)
                            || ((d.AutoPullVerdict == ResultsAutoVerdicts.NotFinished
                                 || d.AutoPullVerdict == ResultsAutoVerdicts.Empty)
                                && d.DateEnd <= stuckUntil && d.DateEnd > stuckFrom)))
            .ToListAsync(ct);
        if (candidates.Count == 0) return [];

        var matches = await new DiscoveryCompetitionMatcher(db).MatchAsync(candidates, ct);
        return candidates
            .Where(d =>
            {
                var inDb = matches.GetValueOrDefault(d.Id) is not null;
                return ResultsAutoWaiting.IsWaiting(d.AutoPullVerdict, d.AutoPullCheckedAt, d.Status, inDb, now)
                       || ResultsAutoWaiting.IsStuck(d.AutoPullVerdict, d.DateEnd, d.Status, inDb, now);
            })
            // Сперва «ждёт тебя» (решение уже сформулировано), потом застрявшие — старые первыми.
            .OrderBy(d => d.AutoPullVerdict == ResultsAutoVerdicts.Blocked ? 0 : 1)
            .ThenBy(d => d.DateEnd)
            .ThenByDescending(d => d.AutoPullCheckedAt)
            .Select(d => new ResultsAutoWaitingRow(
                d.Id, d.OrgCompId, d.Name, d.DateStart, d.DateEnd, d.AutoPullBlockedReason, d.AutoPullCheckedAt,
                d.AutoPullVerdict ?? ResultsAutoVerdicts.Blocked))
            .ToList();
    }
}
