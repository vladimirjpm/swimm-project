using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Перенос всех результатов source-соревнования в target (см. <see cref="IResultTransferService"/>).
/// Один SaveChanges = одна транзакция; dry-run не пишет ничего. Данные результатов
/// денормализованы в публичных выдачах → после применения сбрасываем кэш целиком.
/// </summary>
public class ResultTransferService(SwimmDbContext db, ICacheService cache) : IResultTransferService
{
    public async Task<ResultTransferReport> MoveResultsAsync(
        int sourceCompetitionId, int targetCompetitionId, bool apply, CancellationToken ct = default)
    {
        if (sourceCompetitionId == targetCompetitionId)
            throw new ArgumentException("Источник и цель совпадают.");

        var source = await db.Competitions.FirstOrDefaultAsync(c => c.Id == sourceCompetitionId, ct)
            ?? throw new ArgumentException($"Соревнование-источник #{sourceCompetitionId} не найдено.");
        var target = await db.Competitions.FirstOrDefaultAsync(c => c.Id == targetCompetitionId, ct)
            ?? throw new ArgumentException($"Соревнование-цель #{targetCompetitionId} не найдено.");

        var report = new ResultTransferReport
        {
            SourceId = source.Id, SourceName = source.Name, SourceDate = source.Date,
            TargetId = target.Id, TargetName = target.Name, TargetDate = target.Date,
            ResultsToMove = await db.Results.CountAsync(r => r.CompetitionId == source.Id, ct),
            TargetExistingResults = await db.Results.CountAsync(r => r.CompetitionId == target.Id, ct),
            Applied = false
        };

        // Потенциальные дубли: индивидуальные (не эстафетные) заплывы источника, чей
        // (пловец, стиль, дистанция) уже присутствует в цели.
        var sourceKeys = await db.Results.AsNoTracking()
            .Where(r => r.CompetitionId == source.Id && r.RelayId == null)
            .Select(r => new { r.SwimmerId, r.StyleId, r.Distance })
            .ToListAsync(ct);
        var targetKeys = await db.Results.AsNoTracking()
            .Where(r => r.CompetitionId == target.Id && r.RelayId == null)
            .Select(r => new { r.SwimmerId, r.StyleId, r.Distance })
            .ToListAsync(ct);
        report.OverlapCount = sourceKeys.Intersect(targetKeys).Count();

        if (!apply) return report;

        var targetDate = ParseDate(target.Date);
        var toMove = await db.Results.Where(r => r.CompetitionId == source.Id).ToListAsync(ct);
        foreach (var r in toMove)
        {
            r.CompetitionId = target.Id;
            if (targetDate is DateTime d) r.CompetitionDate = d;
        }

        await db.SaveChangesAsync(ct);   // одна транзакция
        await cache.InvalidateAllAsync();

        report.Applied = true;
        return report;
    }

    /// <summary>dd/MM/yyyy → DateTime (Unspecified, как хранит колонка). null — не распознано.</summary>
    private static DateTime? ParseDate(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        return DateTime.TryParseExact(date.Trim(), "dd/MM/yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt
            : null;
    }
}
