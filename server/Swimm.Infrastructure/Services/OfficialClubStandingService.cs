using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="IOfficialClubStandingService"/>
/// <remarks>
/// Проверка стоит одного похода в loglig за страницей соревнования, одного за таблицей зачёта
/// и до дюжины за заплывами (ради шкалы) — поэтому зовётся на затягивании и в бэкфилле, а не
/// в горячем пути выдач.
/// </remarks>
public class OfficialClubStandingService : IOfficialClubStandingService
{
    private readonly SwimmDbContext _db;
    private readonly ILogligClient _loglig;
    private readonly ILogger<OfficialClubStandingService> _logger;

    public OfficialClubStandingService(
        SwimmDbContext db, ILogligClient loglig, ILogger<OfficialClubStandingService> logger)
    {
        _db = db;
        _loglig = loglig;
        _logger = logger;
    }

    public async Task<OfficialClubStandingProbe> ProbeAsync(int logligId, CancellationToken ct = default)
    {
        var standing = await _loglig.GetCompetitionStandingAsync(logligId, ct: ct);
        if (standing is null)
            return OfficialClubStandingProbe.None(
                $"loglig {logligId} недоступен — проверить официальный клубный зачёт не удалось.");

        if (!standing.HasStanding)
            return OfficialClubStandingProbe.None(
                "Официального клубного зачёта у соревнования нет — наши клубные очки сверять не с чем.");

        var rules = await _db.PointRulesClubs.AsNoTracking().Include(r => r.Entries).ToListAsync(ct);
        var matched = PointRuleScaleMatcher.Match(standing.Scale, rules);

        var scaleText = string.Join(", ", standing.Scale.OrderBy(p => p.Key).Take(10).Select(p => p.Value));
        var message = matched is not null
            ? $"Официальный клубный зачёт есть, шкала совпала с правилом «{matched.Version}» — оно и подставлено."
            : standing.Scale.Count < PointRuleScaleMatcher.MinPlacesForMatch
                ? "Официальный клубный зачёт есть, но шкалу снять не удалось — выберите правило вручную."
                : $"Официальный клубный зачёт есть, но его шкала ({scaleText}…) не совпала ни с одним правилом — " +
                  "заведите правило на /Admin/PointsRules, иначе очки разойдутся с официальными.";

        return new OfficialClubStandingProbe(true, standing.Scale, matched?.Id, matched?.Version, message);
    }

    public async Task<OfficialClubStandingProbe?> ProbeAndStampAsync(int orgCompId, CancellationToken ct = default)
    {
        var logligId = await _db.DiscoveredCompetitions.AsNoTracking()
            .Where(d => d.OrgCompId == orgCompId)
            .Select(d => d.LogligId)
            .FirstOrDefaultAsync(ct);

        if (logligId is not int id)
        {
            _logger.LogInformation(
                "Клубный зачёт: у compID {OrgCompId} нет loglig-id — флаг не проставлен", orgCompId);
            return null;
        }

        var probe = await ProbeAsync(id, ct);
        // «Недоступен» отличаем от «зачёта нет»: в первом случае писать false — соврать.
        if (!probe.HasStanding && probe.Message.Contains("недоступен"))
            return null;

        await StampAsync(orgCompId, probe.HasStanding, ct);
        return probe;
    }

    public async Task<OfficialClubStandingBackfillReport> BackfillAsync(
        bool force = false, CancellationToken ct = default)
    {
        // Один поход на соревнование, а не на день: у многодневки OrgCompId общий.
        var targets = await _db.Competitions.AsNoTracking()
            .Where(c => c.OrgCompId != null && (force || c.HasOfficialClubStanding == null))
            .Select(c => new { OrgCompId = c.OrgCompId!.Value, c.Name, c.Date })
            .ToListAsync(ct);

        var lines = new List<string>();
        int withStanding = 0, without = 0, unknown = 0;

        foreach (var group in targets.GroupBy(t => t.OrgCompId).OrderBy(g => g.Key))
        {
            var head = group.First();
            var probe = await ProbeAndStampAsync(group.Key, ct);

            if (probe is null)
            {
                unknown++;
                lines.Add($"?  compID {group.Key} · {head.Name} — не проверено (нет loglig-id или сайт недоступен)");
                continue;
            }

            if (probe.HasStanding)
            {
                withStanding++;
                var rule = probe.MatchedRuleVersion is null ? "шкала не опознана" : $"шкала = {probe.MatchedRuleVersion}";
                lines.Add($"✓  compID {group.Key} · {head.Name} — зачёт ЕСТЬ ({rule})");
            }
            else
            {
                without++;
                lines.Add($"–  compID {group.Key} · {head.Name} — зачёта нет");
            }
        }

        return new OfficialClubStandingBackfillReport(
            withStanding + without, withStanding, without, unknown, lines);
    }

    /// <summary>Флаг — свойство соревнования, поэтому проставляется всем его дням.</summary>
    private async Task StampAsync(int orgCompId, bool hasStanding, CancellationToken ct)
    {
        // Дни многодневки штампуются через событие: OrgCompId стоит не у каждого дня.
        var eventIds = await _db.Competitions.AsNoTracking()
            .Where(c => c.OrgCompId == orgCompId && c.EventId != null)
            .Select(c => c.EventId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var days = await _db.Competitions
            .Where(c => c.OrgCompId == orgCompId || (c.EventId != null && eventIds.Contains(c.EventId.Value)))
            .ToListAsync(ct);

        if (days.Count == 0) return;

        foreach (var day in days)
            day.HasOfficialClubStanding = hasStanding;

        await _db.SaveChangesAsync(ct);
    }
}
