using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="ICollapsedDaysReviewService"/>
/// <remarks>
/// Первый класс берёт план у <see cref="ICompetitionDaySplitService"/> (dry-run), а не считает
/// раскладку сам: обзор и деление обязаны видеть одно и то же (правило 1 data-integrity).
/// </remarks>
public sealed class CollapsedDaysReviewService(
    SwimmDbContext db,
    IDisciplineDateSource dateSource,
    ICompetitionDaySplitService splitter) : ICollapsedDaysReviewService
{
    /// <summary>
    /// Окно «дни одного старта»: дни Эйлата-2025 — 06, 08 и 11/03 (пять дней от первого до
    /// последнего), а одноимённые туры лиг идут раз в две-три недели.
    /// </summary>
    internal const int SameEventWindowDays = 7;

    private const string DateFormat = "dd/MM/yyyy";

    public async Task<CollapsedDaysReview> ReviewAsync(
        TimeSpan pause, Action<string>? progress = null, CancellationToken ct = default)
    {
        var withoutEvent = await LoadDaysWithoutEventAsync(ct);
        // Соревнования из второго класса сетку не спрашиваем: их строки и так на своих днях,
        // делить там нечего — нужен сбор в событие.
        var grouped = withoutEvent.SelectMany(g => g.Days).Select(d => d.CompetitionId).ToHashSet();

        var discovered = await db.DiscoveredCompetitions.AsNoTracking()
            .Where(d => d.LogligId != null)
            .OrderBy(d => d.Id)
            .Select(d => new { d.Id, LogligId = d.LogligId!.Value, d.OrgCompId })
            .ToListAsync(ct);

        var collapsed = new List<CollapsedDaysCandidate>();
        var failed = new List<string>();
        var checkedCount = 0;
        foreach (var row in discovered)
        {
            // Только «один день без события»: не импортированный старт и уже собранное событие — мимо.
            var days = await CompetitionIdentity.ResolveDaysAsync(db, row.OrgCompId, ct);
            if (days is not [{ EventId: null } single] || grouped.Contains(single.Id)) continue;

            if (checkedCount++ > 0) await Task.Delay(pause, ct);
            try
            {
                var dates = await dateSource.FetchAsync(row.LogligId, ct);
                progress?.Invoke($"#{single.Id} {single.Date} loglig {row.LogligId}: дней по сетке {dates.Days.Count}");
                if (dates.Days.Count < 2) continue;

                await Task.Delay(pause, ct);
                var plan = await splitter.SplitAsync(single.Id, row.LogligId, apply: false, ct);
                collapsed.Add(new CollapsedDaysCandidate(row.Id, row.LogligId, row.OrgCompId, dates.Days, plan));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed.Add($"#{single.Id} «{single.Name}» (discovery #{row.Id}, loglig {row.LogligId}): {ex.Message}");
            }
        }

        return new CollapsedDaysReview(checkedCount, collapsed, withoutEvent, failed);
    }

    private async Task<List<DaysWithoutEventGroup>> LoadDaysWithoutEventAsync(CancellationToken ct)
    {
        var comps = await db.Competitions.AsNoTracking()
            .Where(c => c.EventId == null)
            .Select(c => new { c.Id, c.Name, c.PoolType, c.Date, c.OrgCompId })
            .ToListAsync(ct);

        var groups = GroupDaysWithoutEvent(comps
            .Select(c => (c.Name, c.PoolType, c.Id, c.OrgCompId, Date: ParseDate(c.Date)))
            .Where(c => c.Date != null)
            .Select(c => (c.Name, c.PoolType, new DaysWithoutEventDay(c.Id, c.Date!.Value, c.OrgCompId, 0))));
        if (groups.Count == 0) return groups;

        var ids = groups.SelectMany(g => g.Days).Select(d => d.CompetitionId).ToArray();
        var rows = await db.Results.AsNoTracking()
            .Where(r => ids.Contains(r.CompetitionId))
            .GroupBy(r => r.CompetitionId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return groups
            .Select(g => g with { Days = g.Days.Select(d => d with { Rows = rows.GetValueOrDefault(d.CompetitionId) }).ToList() })
            .ToList();
    }

    /// <summary>
    /// Чистая часть второго класса: одно имя и бассейн, дни цепочкой не дальше
    /// <see cref="SameEventWindowDays"/> от первого дня группы. Группа — от двух дней.
    /// </summary>
    internal static List<DaysWithoutEventGroup> GroupDaysWithoutEvent(
        IEnumerable<(string Name, string PoolType, DaysWithoutEventDay Day)> competitions)
    {
        var result = new List<DaysWithoutEventGroup>();
        foreach (var byName in competitions.GroupBy(c => (Name: c.Name.Trim(), c.PoolType)))
        {
            var cluster = new List<DaysWithoutEventDay>();
            foreach (var (_, _, day) in byName.OrderBy(c => c.Day.Date).ThenBy(c => c.Day.CompetitionId))
            {
                if (cluster.Count > 0 && day.Date.DayNumber - cluster[0].Date.DayNumber > SameEventWindowDays)
                {
                    Flush();
                    cluster = [];
                }
                cluster.Add(day);
            }
            Flush();

            void Flush()
            {
                if (cluster.Count > 1)
                    result.Add(new DaysWithoutEventGroup(byName.Key.Name, byName.Key.PoolType, cluster));
            }
        }
        return result.OrderBy(g => g.Days[0].Date).ToList();
    }

    private static DateOnly? ParseDate(string? date) =>
        DateOnly.TryParseExact(date?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
}
