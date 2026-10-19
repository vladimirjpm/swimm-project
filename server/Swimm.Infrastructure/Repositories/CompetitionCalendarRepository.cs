using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Чтение для календаря сезона (docs/plans/competitions-home-plan.md, Б1). Сборка и правила —
/// <see cref="CompetitionCalendarBuilder"/>.
///
/// Два контекста намеренно, как у <see cref="HubGroupPublicRepository"/>: всё публичное — через
/// <see cref="SwimmReadDbContext"/> (swimm_ro), а площадка (решение В5) лежит только во «Входящих»
/// (<c>Sys_DiscoveredCompetitions</c>, гранта swimm_ro нет) — её читает <see cref="SwimmDbContext"/>,
/// и только безопасные поля: compID, имя, площадка, даты (и первая объявленная — перенос), вид
/// спорта, статус, «пропал ли из списка». С части В (решение В9) — ещё и весь сезон «Входящих»:
/// будущие старты, которых нет в справочнике, тоже идут в календарь.
///
/// Кэш — в контроллере (<c>CachedJson</c>); метки таблиц запись получает сама (К3), включая
/// «Входящие», так что новая площадка или затянутый день сбрасывают календарь.
/// </summary>
public class CompetitionCalendarRepository : ICompetitionCalendarRepository
{
    private readonly SwimmReadDbContext _read;
    private readonly SwimmDbContext _rw;

    public CompetitionCalendarRepository(SwimmReadDbContext read, SwimmDbContext rw)
    {
        _read = read;
        _rw = rw;
    }

    public async Task<CompetitionCalendarDto> GetSeasonAsync(int season, DateOnly today, CancellationToken ct = default)
    {
        // ── 1. Все дни справочника — лёгкая проекция: дата лежит строкой dd/MM/yyyy, поэтому
        // отбор по сезону — в памяти (тот же приём, что у /api/competitions).
        var all = await _read.Competitions.AsNoTracking()
            .Select(c => new { c.Id, c.Date, c.EventId })
            .ToListAsync(ct);

        var dated = all
            .Select(c => (c.Id, c.EventId, Date: ParseDate(c.Date)))
            .ToList();

        var seasons = dated
            .Where(c => c.Date is not null)
            .Select(c => SeasonMath.StartYearOf(c.Date!.Value.ToDateTime(TimeOnly.MinValue)))
            .Append(SeasonMath.StartYearOf(today.ToDateTime(TimeOnly.MinValue)))
            .Distinct()
            .Order()
            .ToList();

        // Событие берём целиком, если хоть один его день в сезоне: сезон старта решает билдер
        // по первому дню.
        bool InSeason(DateOnly? d) => d is not null
            && SeasonMath.StartYearOf(d.Value.ToDateTime(TimeOnly.MinValue)) == season;
        var eventIds = dated.Where(c => c.EventId is not null && InSeason(c.Date))
            .Select(c => c.EventId!.Value).ToHashSet();
        var ids = dated.Where(c => InSeason(c.Date) || (c.EventId is int e && eventIds.Contains(e)))
            .Select(c => c.Id).ToList();
        var dateById = dated.ToDictionary(c => c.Id, c => c.Date);

        // ── 2. Детали дней сезона.
        var details = await _read.Competitions.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new
            {
                c.Id,
                c.EventId,
                EventName = c.Event != null ? c.Event.Name : null,
                EventOrgCompId = c.Event != null ? c.Event.OrgCompId : null,
                c.Name,
                c.PoolType,
                c.IsChampionship,
                c.IsMasters,
                c.Discipline,
                c.StandingKindOverride,
                c.OrgCompId,
                HasResults = _read.Results.Any(r => r.CompetitionId == c.Id),
            })
            .ToListAsync(ct);

        var categoryRows = await _read.CategoryCompetitions.AsNoTracking()
            .Where(cc => ids.Contains(cc.CompetitionId))
            .Select(cc => new { cc.CompetitionId, cc.Category.Key })
            .ToListAsync(ct);
        var categoriesById = categoryRows.GroupBy(r => r.CompetitionId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<string>)g.Select(r => r.Key).ToList());

        var sourceRows = await _read.CompetitionSources.AsNoTracking()
            .Where(s => ids.Contains(s.CompetitionId))
            .Select(s => new { s.CompetitionId, s.OrgCompId })
            .ToListAsync(ct);
        var sourcesById = sourceRows.GroupBy(r => r.CompetitionId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<int>)g.Select(r => r.OrgCompId).ToList());

        // ── 3. Заявки: дни сезона + уже привязанные к дням сезона. Kind=Unspecified обязателен:
        // CompDate — `timestamp without time zone` (ловушка Npgsql, см. StartListPublicRepository).
        var (from, toExclusive) = SeasonMath.RangeOf(season);
        var fromDate = DateTime.SpecifyKind(from, DateTimeKind.Unspecified);
        var toDate = DateTime.SpecifyKind(toExclusive, DateTimeKind.Unspecified);
        var entryRows = await _read.CompetitionEntries.AsNoTracking()
            .Where(e => (e.CompDate >= fromDate && e.CompDate < toDate)
                        || (e.CompetitionId != null && ids.Contains(e.CompetitionId.Value)))
            .Select(e => new { e.OrgCompId, e.CompetitionId, e.CompDate, e.CompName })
            .Distinct()
            .ToListAsync(ct);
        var entries = entryRows
            .GroupBy(e => (e.OrgCompId, e.CompetitionId, Day: DateOnly.FromDateTime(e.CompDate)))
            .Select(g => new CalendarEntryRow
            {
                OrgCompId = g.Key.OrgCompId,
                CompetitionId = g.Key.CompetitionId,
                Date = g.Key.Day,
                CompName = g.First().CompName,
            })
            .ToList();

        // ── 4. Площадка и вид спорта из «Входящих», чемпионат предстоящих — из справки о старте.
        var orgCompIds = details.Select(d => d.OrgCompId)
            .Concat(details.Select(d => d.EventOrgCompId))
            .Where(x => x is not null).Select(x => x!.Value)
            .Concat(sourceRows.Select(s => s.OrgCompId))
            .Concat(entries.Select(e => e.OrgCompId))
            .Distinct()
            .ToList();

        // Плюс весь сезон «Входящих» (В9): строки, у которых дата начала — нынешняя или первая
        // объявленная (перенос, В4) — в сезоне. Даты там — timestamptz с UTC-полночью, поэтому
        // границы сезона — Utc (не Unspecified, как у CompDate выше).
        var seasonFromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var seasonToUtc = DateTime.SpecifyKind(toExclusive, DateTimeKind.Utc);
        var discovered = await _rw.DiscoveredCompetitions.AsNoTracking()
            .Where(d => orgCompIds.Contains(d.OrgCompId)
                        || (d.DateStart >= seasonFromUtc && d.DateStart < seasonToUtc)
                        || (d.PreviousDateStart != null && d.PreviousDateStart >= seasonFromUtc && d.PreviousDateStart < seasonToUtc))
            .Select(d => new
            {
                d.OrgCompId, d.Name, d.Venue, d.DateStart, d.DateEnd, d.Discipline, d.Status,
                d.PreviousDateStart, d.PreviousDateEnd, Missing = d.MissingSince != null,
            })
            .ToListAsync(ct);

        // Справка о старте (чемпионат) — для заявок и для будущих стартов из «Входящих».
        var entryOrgCompIds = entries.Select(e => e.OrgCompId)
            .Concat(discovered.Select(d => d.OrgCompId))
            .Distinct().ToList();
        var meetInfos = entryOrgCompIds.Count == 0 ? [] : await _read.CompetitionMeetInfos.AsNoTracking()
            .Where(m => entryOrgCompIds.Contains(m.OrgCompId))
            .Select(m => new { m.OrgCompId, m.IsChampionship, m.IsChampionshipOverride })
            .ToListAsync(ct);

        var bands = await _read.Categories.AsNoTracking()
            .Select(c => new { c.Key, c.MinAge, c.MaxAge })
            .ToListAsync(ct);

        var inputs = new CalendarInputs
        {
            Competitions = details.Select(d => new CalendarCompetitionRow
            {
                Id = d.Id,
                EventId = d.EventId,
                EventName = d.EventName,
                EventOrgCompId = d.EventOrgCompId,
                Name = d.Name,
                Date = dateById.GetValueOrDefault(d.Id),
                PoolType = d.PoolType ?? "",
                IsChampionship = d.IsChampionship,
                IsMasters = d.IsMasters,
                Discipline = d.Discipline,
                StandingKindOverride = d.StandingKindOverride,
                OrgCompId = d.OrgCompId,
                HasResults = d.HasResults,
                CategoryKeys = categoriesById.GetValueOrDefault(d.Id) ?? [],
                SourceOrgCompIds = sourcesById.GetValueOrDefault(d.Id) ?? [],
            }).ToList(),
            Entries = entries,
            Discovered = discovered.Select(d => new CalendarDiscoveredRow
            {
                OrgCompId = d.OrgCompId,
                Name = d.Name,
                Venue = d.Venue,
                Start = DateOnly.FromDateTime(d.DateStart),
                End = DateOnly.FromDateTime(d.DateEnd),
                Discipline = d.Discipline,
                PreviousStart = d.PreviousDateStart is DateTime ps ? DateOnly.FromDateTime(ps) : null,
                PreviousEnd = d.PreviousDateEnd is DateTime pe ? DateOnly.FromDateTime(pe) : null,
                Missing = d.Missing,
                Status = d.Status,
            }).ToList(),
            MeetInfoChampionship = meetInfos
                .GroupBy(m => m.OrgCompId)
                .ToDictionary(g => g.Key, g => g.Any(m => m.IsChampionshipOverride ?? m.IsChampionship)),
            CategoryBands = bands
                .GroupBy(b => b.Key)
                .ToDictionary(g => g.Key, g => new CalendarCategoryBand(g.First().MinAge, g.First().MaxAge)),
        };

        return CompetitionCalendarBuilder.Build(season, today, seasons, inputs);
    }

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParseExact(s, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
}
