using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Сшивка заявок с результатами (docs/plans/start-list-plan.md, шаг С9).
///
/// Что делает, по порядку:
/// 1. привязывает заявку ко ДНЮ справочника (<c>CompetitionEntry.CompetitionId</c>) — до
///    импорта его не было и быть не могло (§3.1 плана);
/// 2. ищет заявке её результат и ставит <c>Status = swum</c>;
/// 3. оставшимся ставит <c>no-show</c> — это и есть неявки дня старта.
///
/// Матчинг двумя проходами, по той же причине, что и в <see cref="StartListMatcher"/>:
/// заплыв и дорожку в день старта переставляют (снятия сдвигают посев), поэтому точный
/// ключ «дорожка» дополняется мягким «тот же пловец в той же дисциплине». Мягкий проход
/// срабатывает, только когда кандидат С ОБЕИХ сторон РОВНО ОДИН: у дисциплины бывают
/// предварительные и финал, и приписать заявку не тому заплыву значило бы соврать про то,
/// чем закончился старт.
///
/// Идемпотентна: гоняется после каждого импорта, в том числе повторного.
/// </summary>
public sealed class StartListStitchService : IStartListStitchService
{
    private readonly SwimmDbContext _db;
    private readonly ILogger<StartListStitchService> _logger;

    public StartListStitchService(SwimmDbContext db, ILogger<StartListStitchService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StartListStitchReport>> StitchCompetitionsAsync(
        IReadOnlyCollection<int> competitionIds, CancellationToken ct = default)
    {
        if (competitionIds.Count == 0) return [];

        // Штамп compID стоит либо на самом дне, либо на событии целиком — оба случая
        // законны (CompetitionIdentity). Собираем оба.
        var orgCompIds = await _db.Competitions.AsNoTracking()
            .Where(c => competitionIds.Contains(c.Id))
            .Select(c => c.OrgCompId ?? (c.Event != null ? c.Event.OrgCompId : null))
            .Where(id => id != null)
            .Select(id => id!.Value)
            .Distinct()
            .ToListAsync(ct);

        var reports = new List<StartListStitchReport>();
        foreach (var orgCompId in orgCompIds)
            reports.Add(await StitchAsync(orgCompId, ct));

        return reports;
    }

    public async Task<StartListStitchReport> StitchAsync(int orgCompId, CancellationToken ct = default)
    {
        var entries = await _db.CompetitionEntries
            .Where(e => e.OrgCompId == orgCompId)
            .ToListAsync(ct);

        if (entries.Count == 0)
            return new StartListStitchReport(orgCompId, 0, 0, 0, 0, 0, 0, 0);

        var days = await CompetitionIdentity.ResolveDaysAsync(_db, orgCompId, ct);
        if (days.Count == 0)
        {
            // Протокол ещё не импортирован — заявки остаются «entered». Это НЕ повод
            // объявлять их неявками: неявка определяется по загруженному протоколу.
            _logger.LogInformation(
                "Сшивка {OrgCompId}: соревнования нет в справочнике, заявок {Count} — ждут импорта",
                orgCompId, entries.Count);
            return new StartListStitchReport(orgCompId, 0, entries.Count, 0, 0, 0, 0, entries.Count);
        }

        var dayByDate = new Dictionary<DateTime, Competition>();
        foreach (var day in days)
            if (ParseDate(day.Date) is DateTime d) dayByDate.TryAdd(d, day);

        var dayIds = days.Select(d => d.Id).ToList();
        var results = await _db.Results.AsNoTracking()
            .Where(r => dayIds.Contains(r.CompetitionId))
            .Select(r => new ResultKeyRow(r.Id, r.CompetitionId, r.SwimmerId, r.StyleId, r.Distance, r.Heat, r.Lane))
            .ToListAsync(ct);

        var linked = 0;
        var unlinked = 0;
        foreach (var entry in entries)
        {
            var day = dayByDate.GetValueOrDefault(entry.CompDate.Date)
                      // Единственный день — дата спорить не о чем: у однодневного старта
                      // расхождение на сутки бывает от того, что источник и протокол
                      // печатают разные календарные дни одного соревнования.
                      ?? (days.Count == 1 ? days[0] : null);

            if (day is null) { unlinked++; continue; }

            entry.CompetitionId = day.Id;
            linked++;
        }

        var scoped = entries.Where(e => e.CompetitionId is not null).ToList();
        var (matched, byDiscipline) = MatchToResults(scoped, results);

        var swum = 0;
        var noShow = 0;
        foreach (var entry in scoped)
        {
            if (matched.TryGetValue(entry.Id, out var resultId))
            {
                entry.ResultId = resultId;
                entry.Status = CompetitionEntryStatus.Swum;
                swum++;
            }
            else
            {
                entry.ResultId = null;
                entry.Status = CompetitionEntryStatus.NoShow;
                noShow++;
            }
        }

        await _db.SaveChangesAsync(ct);

        return new StartListStitchReport(
            orgCompId, days.Count, entries.Count, linked, swum, noShow, byDiscipline, unlinked);
    }

    private sealed record ResultKeyRow(
        long Id, int CompetitionId, int SwimmerId, int StyleId, string Distance, int Heat, int Lane);

    /// <summary>
    /// Заявка → результат, два прохода: точная дорожка, затем «тот же пловец в той же
    /// дисциплине» при единственном кандидате с обеих сторон.
    /// </summary>
    private static (Dictionary<long, long> Matched, int ByDiscipline) MatchToResults(
        List<CompetitionEntry> entries, List<ResultKeyRow> results)
    {
        var matched = new Dictionary<long, long>();
        var usedResults = new HashSet<long>();

        var byLane = results
            .GroupBy(r => (r.CompetitionId, r.SwimmerId, r.StyleId, Distance: Norm(r.Distance), r.Heat, r.Lane))
            .ToDictionary(g => g.Key, g => g.ToList());

        var leftovers = new List<CompetitionEntry>();
        foreach (var e in entries)
        {
            var key = (e.CompetitionId!.Value, e.SwimmerId, e.StyleId, Norm(e.Distance), e.Heat, e.Lane);
            var hit = byLane.TryGetValue(key, out var list)
                ? list.FirstOrDefault(r => !usedResults.Contains(r.Id))
                : null;

            if (hit is null) { leftovers.Add(e); continue; }

            matched[e.Id] = hit.Id;
            usedResults.Add(hit.Id);
        }

        // Проход 2: пловца пересадили на другую дорожку уже в день старта.
        var byDiscipline = 0;
        var freeResults = results
            .Where(r => !usedResults.Contains(r.Id))
            .GroupBy(r => (r.CompetitionId, r.SwimmerId, r.StyleId, Distance: Norm(r.Distance)))
            .ToDictionary(g => g.Key, g => g.ToList());

        var leftoverByDiscipline = leftovers
            .GroupBy(e => (e.CompetitionId!.Value, e.SwimmerId, e.StyleId, Distance: Norm(e.Distance)))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (key, candidates) in leftoverByDiscipline)
        {
            if (candidates.Count != 1) continue;
            if (!freeResults.TryGetValue(key, out var free) || free.Count != 1) continue;

            matched[candidates[0].Id] = free[0].Id;
            usedResults.Add(free[0].Id);
            byDiscipline++;
        }

        return (matched, byDiscipline);
    }

    /// <summary>Дистанция сравнивается без регистра: источник печатает «4X50», протокол — «4x50».</summary>
    private static string Norm(string distance) => distance.Trim().ToUpperInvariant();

    private static DateTime? ParseDate(string raw) =>
        DateTime.TryParseExact(raw, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.Date
            : null;
}
