using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="ICompetitionSourceAdminService"/>
public class CompetitionSourceAdminService : ICompetitionSourceAdminService
{
    private readonly SwimmDbContext _db;
    private readonly ICacheService _cache;

    public CompetitionSourceAdminService(SwimmDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    public Task<CompetitionSourcesViewDto> GetAsync(int competitionId, CancellationToken ct = default)
        => BuildAsync(competitionId, ct);

    public async Task<CompetitionSourcesViewDto> LinkAsync(
        int competitionId, int orgCompId, CancellationToken ct = default)
    {
        var comp = await _db.Competitions.FirstOrDefaultAsync(c => c.Id == competitionId, ct)
                   ?? throw new InvalidOperationException($"Соревнование {competitionId} не найдено");

        var exists = await _db.CompetitionSources
            .AnyAsync(s => s.CompetitionId == competitionId && s.OrgCompId == orgCompId, ct);
        if (!exists)
        {
            var disc = await _db.DiscoveredCompetitions.AsNoTracking()
                .Where(d => d.OrgCompId == orgCompId)
                .Select(d => new { d.Name, d.DateStart })
                .FirstOrDefaultAsync(ct);

            _db.CompetitionSources.Add(new CompetitionSource
            {
                CompetitionId = competitionId,
                OrgCompId = orgCompId,
                // Колонка календарная (timestamp without time zone) — Kind обязателен
                // Unspecified, иначе Npgsql отвергнет timestamptz-значение из «Входящих».
                SourceDate = disc != null
                    ? DateTime.SpecifyKind(disc.DateStart, DateTimeKind.Unspecified)
                    : ParseDay(comp.Date),
                SourceName = disc?.Name ?? comp.SubName ?? comp.Name,
                SortOrder = 0
            });
            await _db.SaveChangesAsync(ct);
            await InvalidateOverviewAsync();
        }

        return await BuildAsync(competitionId, ct);
    }

    public async Task<CompetitionSourcesViewDto> UnlinkAsync(
        int competitionId, int orgCompId, CancellationToken ct = default)
    {
        var link = await _db.CompetitionSources
            .FirstOrDefaultAsync(s => s.CompetitionId == competitionId && s.OrgCompId == orgCompId, ct);
        if (link != null)
        {
            // Заявки НЕ трогаем: они принадлежат compID федерации, а не нашей привязке.
            // Ошибочную привязку так можно снять и переставить, ничего не перезабирая.
            _db.CompetitionSources.Remove(link);
            await _db.SaveChangesAsync(ct);
            await InvalidateOverviewAsync();
        }

        return await BuildAsync(competitionId, ct);
    }

    private async Task<CompetitionSourcesViewDto> BuildAsync(int competitionId, CancellationToken ct)
    {
        var comp = await _db.Competitions.AsNoTracking()
            .Where(c => c.Id == competitionId)
            .Select(c => new { c.Id, c.Name, c.EventId })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Соревнование {competitionId} не найдено");

        // Дни события целиком: источник привязывается к конкретному дню, и выбрать его
        // человек должен из полного списка, а не из одного открытого.
        var days = await _db.Competitions.AsNoTracking()
            .Where(c => comp.EventId != null ? c.EventId == comp.EventId : c.Id == comp.Id)
            .Select(c => new { c.Id, c.Date, c.DayNumber, c.SubName })
            .ToListAsync(ct);

        var orderedDays = days
            .OrderBy(d => d.DayNumber ?? int.MaxValue)
            .ThenBy(d => ParseDay(d.Date) ?? DateTime.MaxValue)
            .Select(d => new CompetitionSourceDayDto(d.Id, d.Date, d.DayNumber, d.SubName))
            .ToList();

        var dayIds = days.Select(d => d.Id).ToList();

        var links = await _db.CompetitionSources.AsNoTracking()
            .Where(s => dayIds.Contains(s.CompetitionId))
            .Select(s => new { s.CompetitionId, s.OrgCompId, s.SourceDate, s.SourceName, s.SortOrder })
            .ToListAsync(ct);

        var linkedOrgIds = links.Select(l => l.OrgCompId).ToList();
        var counts = await _db.CompetitionEntries.AsNoTracking()
            .Where(e => linkedOrgIds.Contains(e.OrgCompId))
            .GroupBy(e => e.OrgCompId)
            .Select(g => new { OrgCompId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OrgCompId, x => x.Count, ct);

        var linked = links
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.SourceDate ?? DateTime.MaxValue)
            .ThenBy(l => l.OrgCompId)
            .Select(l => new CompetitionSourceLinkDto(
                l.OrgCompId, l.CompetitionId,
                l.SourceDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                l.SourceName,
                counts.TryGetValue(l.OrgCompId, out var c) ? c : 0))
            .ToList();

        // Кандидаты — по датам дней ± сутки: окружные протоколы одного чемпионата стоят в
        // календаре федерации рядом, а сутки запаса покрывают расхождение дат в источнике.
        var dayDates = days.Select(d => ParseDay(d.Date)).Where(d => d != null).Select(d => d!.Value).ToList();
        var candidates = new List<CompetitionSourceCandidateDto>();
        if (dayDates.Count > 0)
        {
            var from = dayDates.Min().AddDays(-1);
            var to = dayDates.Max().AddDays(1);
            // «Входящие» хранят дату как UTC-момент полуночи — сравниваем в том же виде.
            var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
            var toUtc = DateTime.SpecifyKind(to.AddDays(1), DateTimeKind.Utc);

            var rows = await _db.DiscoveredCompetitions.AsNoTracking()
                .Where(d => d.DateStart >= fromUtc && d.DateStart < toUtc)
                .Select(d => new { d.Id, d.OrgCompId, d.Name, d.DateStart, d.Status })
                .ToListAsync(ct);

            // «Привязан к другому соревнованию» — предупреждение, а не фильтр: один и тот же
            // compID у двух наших соревнований почти всегда ошибка, и её надо ВИДЕТЬ.
            var elsewhere = await _db.CompetitionSources.AsNoTracking()
                .Where(s => !dayIds.Contains(s.CompetitionId))
                .Select(s => s.OrgCompId)
                .ToListAsync(ct);
            var elsewhereSet = elsewhere.ToHashSet();

            candidates = rows
                .Where(r => !linkedOrgIds.Contains(r.OrgCompId))
                .OrderBy(r => r.DateStart)
                .Select(r => new CompetitionSourceCandidateDto(
                    r.Id, r.OrgCompId, r.Name,
                    r.DateStart.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    r.Status, elsewhereSet.Contains(r.OrgCompId)))
                .ToList();
        }

        return new CompetitionSourcesViewDto(comp.Id, comp.Name, orderedDays, linked, candidates);
    }

    /// <summary>
    /// Овервью соревнования кэшируется (competition-overview:*), а список источников — его
    /// часть: без сброса подтаб появился бы только через TTL, и правка выглядела бы не
    /// сработавшей. Точечного сброса по префиксу в ICacheService нет — ключ включает весь
    /// фильтр выборки, так что попасть в него мы всё равно не смогли бы; привязка источника
    /// делается редко и вручную, сбросить весь кэш тут дешевле, чем заводить новый шов.
    /// </summary>
    private Task InvalidateOverviewAsync() => _cache.InvalidateAllAsync();

    /// <summary>dd/MM/yyyy → дата (Unspecified, колонка календарная); непарсимая → null.</summary>
    private static DateTime? ParseDay(string? date)
        => date != null && DateTime.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture,
               DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified)
            : null;
}
