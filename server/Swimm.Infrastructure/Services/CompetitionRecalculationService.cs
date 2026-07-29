using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="ICompetitionRecalculationService"/>
public class CompetitionRecalculationService : ICompetitionRecalculationService
{
    private readonly SwimmDbContext _db;

    public CompetitionRecalculationService(SwimmDbContext db) => _db = db;

    public async Task<int> RecalculateCompetitionAsync(int competitionId, CancellationToken ct = default)
    {
        var comp = await _db.Competitions.AsNoTracking()
            .Where(c => c.Id == competitionId)
            .Select(c => new { c.Id, c.EventId, c.ShowCombineAllResults })
            .FirstOrDefaultAsync(ct);
        if (comp is null) return 0;

        // Область — всё событие: объединённый зачёт считается по всем дням.
        var competitionIds = comp.EventId is int eventId
            ? await _db.Competitions.AsNoTracking()
                .Where(c => c.EventId == eventId).Select(c => c.Id).ToListAsync(ct)
            : [comp.Id];

        // Флаг может стоять не у всех дней события — считаем, если он есть хоть у одного,
        // иначе после его включения у одного дня тоггл показал бы наполовину пустую таблицу.
        var anyCombined = await _db.Competitions.AsNoTracking()
            .AnyAsync(c => competitionIds.Contains(c.Id) && c.ShowCombineAllResults, ct);

        return anyCombined
            ? await ApplyCombinedAsync(competitionIds, ct)
            : await ClearCombinedAsync(competitionIds, ct);
    }

    public async Task<int> RecalculateAllCombinedAsync(CancellationToken ct = default)
    {
        // Группируем по событию (однодневные — сами себе событие), чтобы не считать дважды.
        var scopes = await _db.Competitions.AsNoTracking()
            .Where(c => c.ShowCombineAllResults)
            .Select(c => new { c.Id, c.EventId })
            .ToListAsync(ct);

        var total = 0;
        foreach (var group in scopes.GroupBy(c => c.EventId ?? -c.Id))
        {
            var ids = group.Key < 0
                ? group.Select(c => c.Id).ToList()
                : await _db.Competitions.AsNoTracking()
                    .Where(c => c.EventId == group.Key).Select(c => c.Id).ToListAsync(ct);

            total += await ApplyCombinedAsync(ids, ct);
        }

        return total;
    }

    private async Task<int> ApplyCombinedAsync(IReadOnlyCollection<int> competitionIds, CancellationToken ct)
    {
        var rows = await _db.Results.AsNoTracking()
            .Where(r => competitionIds.Contains(r.CompetitionId))
            .Select(r => new
            {
                r.Id,
                r.SwimmerId,
                r.StyleId,
                r.Distance,
                r.Gender,
                r.EventStyleAge,
                r.EventCategory,
                PoolType = r.Competition.PoolType,
                r.TimeMillisecond,
                r.TimeFail
            })
            .ToListAsync(ct);

        if (rows.Count == 0) return 0;

        var assignments = CombinedPlaceCalculator.Calculate(rows
            .Select(r => new CombinedPlaceCalculator.Row(
                r.Id,
                r.SwimmerId,
                CombinedPlaceCalculator.EventKeyOf(r.StyleId, r.Distance, r.PoolType, r.Gender, r.EventCategory, r.EventStyleAge),
                r.TimeMillisecond,
                r.TimeFail))
            .ToList());

        // Пишем батчами по id: строк на событие — тысячи, не миллионы.
        var byId = assignments.ToDictionary(a => a.ResultId);
        var tracked = await _db.Results
            .Where(r => competitionIds.Contains(r.CompetitionId))
            .ToListAsync(ct);

        foreach (var entity in tracked)
        {
            if (!byId.TryGetValue(entity.Id, out var a)) continue;
            entity.CombinedPlace = a.CombinedPlace;
            entity.IsBestResult = a.IsBestResult;
            entity.BestTimeMs = a.BestTimeMs;
        }

        await _db.SaveChangesAsync(ct);
        return tracked.Count;
    }

    /// <summary>Флаг сняли — стираем материализованные значения, чтобы не показывать устаревшее.</summary>
    private async Task<int> ClearCombinedAsync(IReadOnlyCollection<int> competitionIds, CancellationToken ct)
        => await _db.Results
            .Where(r => competitionIds.Contains(r.CompetitionId) && r.CombinedPlace != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.CombinedPlace, (int?)null)
                .SetProperty(r => r.IsBestResult, (bool?)null)
                .SetProperty(r => r.BestTimeMs, (int?)null), ct);
}
