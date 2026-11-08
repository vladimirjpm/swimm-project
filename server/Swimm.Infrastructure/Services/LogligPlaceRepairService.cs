using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="ILogligPlaceRepairService"/>
public sealed class LogligPlaceRepairService(
    SwimmDbContext db,
    ICompetitionDiscoveryProvider provider,
    ICompetitionRecalculationService recalc,
    ILogger<LogligPlaceRepairService> logger) : ILogligPlaceRepairService
{
    public async Task<LogligPlaceReport> RepairAsync(int discoveredId, bool apply, CancellationToken ct = default)
    {
        var discovered = await db.DiscoveredCompetitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == discoveredId, ct)
            ?? throw new InvalidOperationException($"Запись автозабора #{discoveredId} не найдена");

        if (discovered.LogligId is not int logligId)
            throw new InvalidOperationException(
                $"У записи #{discoveredId} нет LogligId — пособытийного источника у неё нет");

        // Все дни события через общий шов: OrgCompId стоит только на одном дне (И-60).
        var competitions = await CompetitionIdentity.ResolveDaysAsync(db, discovered.OrgCompId, ct);
        if (competitions.Count == 0)
            throw new InvalidOperationException(
                $"Соревнование с OrgCompId {discovered.OrgCompId} ещё не импортировано — " +
                "ремонт мест только ПРАВИТ уже существующие строки.");
        var competitionIds = competitions.Select(c => c.Id).ToList();
        var singleDayDate = competitions.Count == 1 ? competitions[0].Date : "";

        var eventIds = await provider.FetchEventIdsAsync(logligId, ct);
        logger.LogInformation("loglig {LogligId}: событий {Count}, сверяю личные места", logligId, eventIds.Count);

        var competitionName = string.Empty;
        var source = new List<PlaceRowFromSource>();
        var events = 0;
        var unreadable = new List<int>();

        foreach (var eventId in eventIds)
        {
            LogligEventResultsDto ev;
            try
            {
                ev = await provider.FetchEventResultsAsync(eventId, ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("loglig событие {EventId} не прочитано: {Error}", eventId, ex.Message);
                unreadable.Add(eventId);
                continue;
            }
            if (competitionName.Length == 0) competitionName = ev.CompetitionName;
            if (ev.IsRelay) continue;

            events++;
            var date = ev.Date.Length > 0 ? ev.Date : singleDayDate;
            foreach (var r in ev.Rows)
            {
                // «00:00.00» страница печатает, когда времени нет, — это «без времени».
                int? timeMs = SwimTime.ParseToMs(r.Time) is int ms and > 0 ? ms : null;
                source.Add(new PlaceRowFromSource(
                    date, ev.StyleName, ev.Distance,
                    LogligPlaceMatcher.SectionGender(r.Category, ev.Gender), r.Category,
                    r.Heat, r.Lane, r.FullName, r.BirthYear, timeMs, r.Position,
                    PlacedWithoutTime: SuspectReasons.IsPlacedWithoutTime(false, timeMs, r.FailNote, r.Position),
                    Event: eventId.ToString(CultureInfo.InvariantCulture), Round: r.Round));
            }
        }

        var rows = await db.Results.AsNoTracking()
            .Where(r => competitionIds.Contains(r.CompetitionId) && r.RelayId == null)
            .Select(r => new
            {
                r.Id, r.CompetitionDate, Style = r.Style!.Name, r.Distance, r.Gender, r.Heat, r.Lane,
                r.Swimmer!.FirstName, r.Swimmer.LastName, r.Swimmer.BirthYear,
                r.TimeMillisecond, r.TimeFail, r.Position
            })
            .ToListAsync(ct);

        var ours = rows.Select(r => new PlaceRowInDb(
                r.Id, r.CompetitionDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                r.Style, r.Distance, r.Gender, r.Heat, r.Lane,
                $"{r.FirstName} {r.LastName}".Trim(), r.BirthYear,
                r.TimeFail ? null : r.TimeMillisecond, r.Position))
            .ToList();

        var plan = LogligPlaceMatcher.Build(source, ours);

        var applied = 0;
        if (apply && plan.CanApply)
            applied = await ApplyAsync(plan, competitionIds, ct);

        return new LogligPlaceReport(competitionName, events, source.Count, ours.Count, applied, plan, unreadable);
    }

    /// <summary>
    /// Записать только место и только в секциях «כללי» (<see cref="PlaceChange.WillApply"/>).
    /// Время, заплыв, дорожка, пол и пловец не трогаются.
    /// </summary>
    private async Task<int> ApplyAsync(PlacePlan plan, IReadOnlyList<int> competitionIds, CancellationToken ct)
    {
        var changed = plan.Changes.Where(c => c.WillApply).ToList();
        if (changed.Count == 0) return 0;

        var ids = changed.Select(c => c.ResultId).ToHashSet();
        var rows = await db.Results.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        foreach (var change in changed)
            rows[change.ResultId].Position = change.PositionAfter;

        await db.SaveChangesAsync(ct);

        // Зачёт и объединённые места материализованы — тот же шов, что у импорта и ремонта эстафет.
        foreach (var competitionId in competitionIds)
            await recalc.RecalculateCompetitionAsync(competitionId, ct);

        logger.LogInformation("Места личных строк: обновлено {Count}", changed.Count);
        return changed.Count;
    }
}
