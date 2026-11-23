using Microsoft.EntityFrameworkCore;
using Swimm.Application.Validation;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Р119: база для <see cref="CollapsedProtocolRule"/> — дни этого compID с числом строк. Одна
/// функция на превью («Затянуть»/«Перезатянуть», пачка, results-auto) и импорт, чтобы они не
/// разошлись (правило 1 data-integrity).
/// </summary>
public static class CollapsedProtocolGuard
{
    /// <summary>
    /// Дни compID (<see cref="CompetitionIdentity.ResolveDaysAsync"/>) без чужого штампа — тот же
    /// круг, что у сопоставления по дате (Р118, <c>BuildLinkedDayIndex</c>): событие из окружных
    /// протоколов держит под одним EventId дни разных compID, и чужие дни дублем не считаются.
    /// </summary>
    public static async Task<CollapsedProtocolFinding?> DetectAsync(
        SwimmDbContext db, int orgCompId, IEnumerable<string?> fileDates, int fileRows, CancellationToken ct = default)
    {
        var dates = fileDates.Select(d => d?.Trim()).Where(d => !string.IsNullOrEmpty(d)).Distinct().ToList();
        if (dates.Count != 1) return null;

        var days = (await CompetitionIdentity.ResolveDaysAsync(db, orgCompId, ct))
            .Where(d => d.OrgCompId is null || d.OrgCompId == orgCompId)
            .ToList();
        if (days.Count == 0) return null;

        var ids = days.Select(d => d.Id).ToList();
        var rows = await db.Results.AsNoTracking()
            .Where(r => ids.Contains(r.CompetitionId))
            .GroupBy(r => r.CompetitionId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return CollapsedProtocolRule.Detect(dates, fileRows,
            days.Select(d => new CollapsedProtocolDay(d.Id, d.Date, rows.GetValueOrDefault(d.Id))));
    }
}
