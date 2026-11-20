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
        var candidates = await db.DiscoveredCompetitions.AsNoTracking()
            .Where(d => d.AutoPullVerdict == ResultsAutoVerdicts.Blocked
                        && d.AutoPullCheckedAt >= since
                        && d.Status == DiscoveredCompetitionStatus.New)
            .ToListAsync(ct);
        if (candidates.Count == 0) return [];

        var matches = await new DiscoveryCompetitionMatcher(db).MatchAsync(candidates, ct);
        return candidates
            .Where(d => ResultsAutoWaiting.IsWaiting(
                d.AutoPullVerdict, d.AutoPullCheckedAt, d.Status, matches.GetValueOrDefault(d.Id) is not null, now))
            .OrderByDescending(d => d.AutoPullCheckedAt)
            .Select(d => new ResultsAutoWaitingRow(
                d.Id, d.OrgCompId, d.Name, d.DateStart, d.DateEnd, d.AutoPullBlockedReason, d.AutoPullCheckedAt))
            .ToList();
    }
}
