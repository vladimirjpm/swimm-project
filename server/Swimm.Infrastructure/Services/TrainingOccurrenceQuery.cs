using Microsoft.EntityFrameworkCore;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Занятия группы из базы — разовые (тренировки с «Ask who's coming») и отмены за даты — для
/// чистых правил <see cref="TrainingOccurrences"/>. Общее для ответов (<see cref="TrainingRsvpService"/>)
/// и плана дорожек (<see cref="LanePlanService"/>: план заводится только на существующее занятие, CF5).
/// </summary>
internal static class TrainingOccurrenceQuery
{
    private static DateTime DayStart(DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    /// <summary>
    /// Разовые занятия группы (тренировки с «Ask who's coming» и заданным началом) за даты
    /// [<paramref name="from"/>; <paramref name="to"/>]. Дата тренировки в базе — полночь UTC
    /// (<c>Sys_TrainingSessions.Date</c> — timestamptz), поэтому границы берём по дню.
    /// </summary>
    public static async Task<List<TrainingOccurrences.OneOff>> OneOffsAsync(
        SwimmDbContext db, int hubGroupId, DateOnly from, DateOnly to)
    {
        var fromDb = DayStart(from);
        var toDb = DayStart(to.AddDays(1));
        var rows = await db.TrainingSessions.AsNoTracking()
            .Where(s => s.HubGroupId == hubGroupId && s.AsksRsvp && s.StartTime != null
                        && s.Date >= fromDb && s.Date < toDb)
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.Date, s.StartTime, s.Title, s.Type, s.PoolType })
            .ToListAsync();
        return rows.Select(r => new TrainingOccurrences.OneOff(
                r.Id, DateOnly.FromDateTime(r.Date), r.StartTime!, r.Title, r.Type,
                string.IsNullOrWhiteSpace(r.PoolType) ? null : r.PoolType))
            .ToList();
    }

    public static async Task<List<TrainingOccurrences.Cancellation>> CancellationsAsync(
        SwimmDbContext db, int hubGroupId, DateOnly from, DateOnly to)
    {
        var rows = await db.HubGroupTrainingCancellations.AsNoTracking()
            .Where(c => c.HubGroupId == hubGroupId && c.Date >= from && c.Date <= to)
            .Select(c => new { c.Date, c.Start, c.Reason })
            .ToListAsync();
        return rows.Select(r => new TrainingOccurrences.Cancellation(r.Date, r.Start, r.Reason)).ToList();
    }

    /// <summary>Занятие с этим ключом (слот расписания или разовое) с пометкой «отменено»; null — такого нет.</summary>
    public static async Task<TrainingOccurrences.Occurrence?> FindAsync(
        SwimmDbContext db, int hubGroupId, GroupTrainingSchedule schedule, DateOnly date, string start)
    {
        var oneOffs = await OneOffsAsync(db, hubGroupId, date, date);
        var cancellations = await CancellationsAsync(db, hubGroupId, date, date);
        return TrainingOccurrences.Find(schedule, oneOffs, cancellations, date, start);
    }

    /// <summary>То же, расписание группы читается здесь; группы нет — null.</summary>
    public static async Task<TrainingOccurrences.Occurrence?> FindAsync(
        SwimmDbContext db, int hubGroupId, DateOnly date, string start)
    {
        var row = await db.HubGroups.AsNoTracking()
            .Where(g => g.Id == hubGroupId)
            .Select(g => new { g.TrainingSchedule })
            .FirstOrDefaultAsync();
        return row == null ? null : await FindAsync(db, hubGroupId, GroupTrainingSchedule.Parse(row.TrainingSchedule), date, start);
    }
}
