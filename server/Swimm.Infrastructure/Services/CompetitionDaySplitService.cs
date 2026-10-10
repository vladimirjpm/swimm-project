using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="ICompetitionDaySplitService"/>
/// <remarks>
/// Решения Влада 07.10.2026 (multiday-split-plan §6): имя дня = имя события, исходное название —
/// в <c>SubName</c>; compID остаётся на делимом соревновании и поднимается на событие (Д2);
/// неоднозначная дисциплина — отказ целиком.
///
/// Только через EF: сохранение само сбрасывает метки кэша своих таблиц (перехватчик К4).
/// </remarks>
public sealed class CompetitionDaySplitService(
    SwimmDbContext db,
    IDisciplineDateSource dateSource,
    ICompetitionRecalculationService recalc) : ICompetitionDaySplitService
{
    private const string DateFormat = "dd/MM/yyyy";

    public async Task<CompetitionDaySplitReport> SplitAsync(
        int competitionId, int? logligId, bool apply, CancellationToken ct = default)
    {
        var comp = await db.Competitions.AsNoTracking().FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (comp == null)
            return CompetitionDaySplitReport.Refused(competitionId, "", logligId, "соревнование не найдено");
        // Событие уже есть. Если в нём другие дни — соревнование уже разделено (или собрано из
        // дней), делить нечего. Если оно ЕДИНСТВЕННЫЙ день своего события — событие заведено
        // импортом на один день, а дни слиплись (Маккабиада-мастерс #1482, событие #5, 10.10.2026):
        // делим, переиспользуя это событие, а не заводя второе.
        int? reuseEvent = null;
        if (comp.EventId is int existingEvent)
        {
            if (await db.Competitions.AnyAsync(c => c.EventId == existingEvent && c.Id != comp.Id, ct))
                return CompetitionDaySplitReport.Refused(comp.Id, comp.Name, logligId,
                    $"уже день события #{existingEvent} — делить нечего");
            reuseEvent = existingEvent;
        }

        var loglig = logligId ?? await ResolveLogligIdAsync(comp, ct);
        if (loglig is not int resolved)
            return CompetitionDaySplitReport.Refused(comp.Id, comp.Name, null,
                "LogligId не найден: у соревнования нет строки discovery — передай его параметром");

        var dates = await dateSource.FetchAsync(resolved, ct);
        var rows = await db.Results.AsNoTracking()
            .Where(r => r.CompetitionId == comp.Id)
            .Select(r => new DayPlanRow(r.Id, r.Style.Name, r.Distance, r.Gender))
            .ToListAsync(ct);
        var plan = DisciplineDayPlanner.Plan(rows, dates);
        var unresolved = plan.Unresolved.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

        if (!plan.CanApply)
        {
            var why = unresolved.Count > 0
                ? $"у {unresolved.Values.Sum()} строк нет однозначного дня — всё или ничего"
                : rows.Count == 0
                    ? "в соревновании нет строк — делить нечего"
                : dates.Days.Count > 1
                    // Сетка многодневная, а все строки — дисциплины одного дня: остальные дни лежат
                    // отдельными соревнованиями (Эйлат-2025, М4 — «дни есть, события нет»).
                    ? $"все строки — дисциплины одного дня ({string.Join(", ", plan.ByDay.Keys.Select(Format))}) " +
                      $"из {dates.Days.Count} по источнику — остальные дни лежат не здесь"
                    : $"по источнику один день ({string.Join(", ", dates.Days.Select(Format))}) — делить нечего";
            return new CompetitionDaySplitReport(comp.Id, comp.Name, resolved, [], unresolved, why, false, null);
        }

        // Само соревнование остаётся днём со СВОЕЙ датой: на нём compID, discovery и источники.
        var existingDate = ParseDate(comp.Date);
        if (existingDate is not { } keep || !plan.ByDay.ContainsKey(keep))
            return CompetitionDaySplitReport.Refused(comp.Id, comp.Name, resolved,
                $"дата соревнования {comp.Date} не совпадает ни с одним днём источника " +
                $"({string.Join(", ", plan.ByDay.Keys.Select(Format))}) — какой день оставить, неясно");

        var rowIds = rows.Select(r => r.ResultId).ToArray();
        var mediaByResult = await db.UserMedia.AsNoTracking()
            .Where(m => m.ResultId != null && rowIds.Contains(m.ResultId.Value))
            .GroupBy(m => m.ResultId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var days = plan.ByDay.Select((kv, i) => new CompetitionDaySplitDay(
                kv.Key, i + 1, kv.Key == keep ? comp.Id : null, kv.Key == keep, kv.Value.Count,
                kv.Value.Sum(id => mediaByResult.GetValueOrDefault(id))))
            .ToList();

        if (!apply)
            return new CompetitionDaySplitReport(comp.Id, comp.Name, resolved, days, unresolved, null, false, null);

        var (eventId, applied) = await ApplyAsync(comp.Id, plan, keep, reuseEvent, ct);

        // Производные величины — по дню (клубный зачёт строится на день; объединённые места и
        // пара-очки пересчёт сам берёт по всему событию).
        foreach (var day in applied)
            await recalc.RecalculateCompetitionAsync(day.CompetitionId!.Value, ct);

        return new CompetitionDaySplitReport(comp.Id, comp.Name, resolved, applied, unresolved, null, true, eventId);
    }

    private async Task<(int EventId, List<CompetitionDaySplitDay> Days)> ApplyAsync(
        int competitionId, DayPlan plan, DateOnly keep, int? reuseEvent, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var comp = await db.Competitions.FirstAsync(c => c.Id == competitionId, ct);
            CompetitionEvent ev;
            if (reuseEvent is int existing)
            {
                // Имя и compID события не трогаем — их ставил импорт; растягиваем только даты.
                ev = await db.CompetitionEvents.FirstAsync(e => e.Id == existing, ct);
                ev.StartDate = plan.ByDay.Keys.Min();
                ev.EndDate = plan.ByDay.Keys.Max();
            }
            else
            {
                ev = new CompetitionEvent
                {
                    Name = comp.Name,
                    OrgCompId = comp.OrgCompId,
                    StartDate = plan.ByDay.Keys.Min(),
                    EndDate = plan.ByDay.Keys.Max(),
                };
                db.CompetitionEvents.Add(ev);
            }
            await db.SaveChangesAsync(ct);

            var subName = comp.SubName ?? comp.Name;
            var memberships = await db.CategoryCompetitions.AsNoTracking()
                .Where(cc => cc.CompetitionId == comp.Id).ToListAsync(ct);

            var result = new List<CompetitionDaySplitDay>();
            var dayNumber = 0;
            foreach (var (date, dayIds) in plan.ByDay)
            {
                dayNumber++;
                var ids = dayIds.ToArray();
                Competition day;
                if (date == keep)
                {
                    day = comp;
                    day.EventId = ev.Id;
                    day.DayNumber = dayNumber;
                    day.SubName = subName;
                }
                else
                {
                    day = NewDay(comp, date, ev.Id, dayNumber, subName);
                    db.Competitions.Add(day);
                    await db.SaveChangesAsync(ct);

                    foreach (var cc in memberships)
                    {
                        var order = await db.CategoryCompetitions
                            .Where(x => x.CategoryId == cc.CategoryId)
                            .MaxAsync(x => (int?)x.DisplayOrder, ct) ?? 0;
                        db.CategoryCompetitions.Add(new CategoryCompetition
                        {
                            CategoryId = cc.CategoryId, CompetitionId = day.Id, DisplayOrder = order + 1,
                        });
                    }
                }

                // Дата строки — дата её дня (и у строк, которые остаются: обе даты обязаны совпасть).
                var rowDate = date.ToDateTime(TimeOnly.MinValue);
                var results = await db.Results.Where(r => ids.Contains(r.Id)).ToListAsync(ct);
                foreach (var r in results)
                {
                    r.CompetitionId = day.Id;
                    r.CompetitionDate = rowDate;
                }

                // Медиа на строке смотрит и на соревнование — едет вслед за строкой.
                var media = await db.UserMedia
                    .Where(m => m.ResultId != null && ids.Contains(m.ResultId.Value)).ToListAsync(ct);
                foreach (var m in media) m.CompetitionId = day.Id;

                await db.SaveChangesAsync(ct);
                result.Add(new CompetitionDaySplitDay(date, dayNumber, day.Id, date == keep, ids.Length, media.Count));
            }

            await tx.CommitAsync(ct);
            return (ev.Id, result);
        });
    }

    /// <summary>
    /// Новый день — копия настроек делимого соревнования. Не копируются: compID (уникален, остаётся
    /// на делимом и на событии) и отметки «очки сверены» — сверка была по сумме, её делать заново.
    /// </summary>
    private static Competition NewDay(Competition source, DateOnly date, int eventId, int dayNumber, string subName) => new()
    {
        Name = source.Name,
        CountryId = source.CountryId,
        Date = Format(date),
        PoolType = source.PoolType,
        IsMasters = source.IsMasters,
        IsAward = source.IsAward,
        IsChampionship = source.IsChampionship,
        HasSplits = source.HasSplits,
        ShowCombineAllResults = source.ShowCombineAllResults,
        EventId = eventId,
        DayNumber = dayNumber,
        SubName = subName,
        Discipline = source.Discipline,
        QualityScannedAt = source.QualityScannedAt,
        PointRuleClubsId = source.PointRuleClubsId,
        StandingKindOverride = source.StandingKindOverride,
        ClubPointsDisabled = source.ClubPointsDisabled,
        HasOfficialClubStanding = source.HasOfficialClubStanding,
        PointRuleSwimmersId = source.PointRuleSwimmersId,
    };

    /// <summary>LogligId из discovery — по compID соревнования или его источников (Д2).</summary>
    private async Task<int?> ResolveLogligIdAsync(Competition comp, CancellationToken ct)
    {
        var orgIds = await db.CompetitionSources.AsNoTracking()
            .Where(s => s.CompetitionId == comp.Id).Select(s => s.OrgCompId).ToListAsync(ct);
        if (comp.OrgCompId is int own) orgIds.Insert(0, own);

        foreach (var org in orgIds.Distinct())
        {
            var id = await db.DiscoveredCompetitions.AsNoTracking()
                .Where(d => d.OrgCompId == org && d.LogligId != null)
                .Select(d => d.LogligId).FirstOrDefaultAsync(ct);
            if (id is int found) return found;
        }
        return null;
    }

    private static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static DateOnly? ParseDate(string? date) =>
        DateOnly.TryParseExact(date?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
}
