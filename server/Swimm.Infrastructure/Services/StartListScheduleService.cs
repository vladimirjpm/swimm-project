using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Плановый обход стартовых протоколов (docs/plans/start-list-plan.md, шаг С10; частота — П3,
/// docs/plans/prod-auto-pull-plan.md §4.3).
///
/// Порядок: раз в сутки (первый тик дня по Израилю, <see cref="StartListSweepRules.DetailsDue"/>)
/// дочитать <c>logligId</c> стартам окна, у которых его нет (С2); затем по каждому старту окна
/// решить правилом <see cref="StartListSweepRules.Decide"/>, пора ли его смотреть в этот тик, и
/// затянуть протокол у тех, кому пора. Решение берёт данные из журнала заборов
/// <c>Sys_StartListPulls</c> и (в день старта) время первого заплыва из <c>CompetitionEntries</c>.
///
/// Скоуп на соревнование, а не на весь обход: забор одного чемпионата — под две сотни
/// запросов с вежливой паузой, то есть минуты, и держать один <c>DbContext</c> (с его
/// change tracker-ом) на всё окно нельзя. Поэтому сюда приходит <see cref="IServiceScopeFactory"/>,
/// а не готовый <c>IStartListPullService</c>.
/// </summary>
public sealed class StartListScheduleService : IStartListScheduleService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StartListScheduleService> _logger;

    public StartListScheduleService(
        IServiceScopeFactory scopeFactory, ILogger<StartListScheduleService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<StartListSweepReport> RunAsync(
        int daysAhead, DateTime? previousRunAt = null, CancellationToken ct = default)
    {
        var window = Math.Max(1, daysAhead);
        var now = DateTime.UtcNow;
        var today = StartListSweepRules.IsraelToday(now);
        var horizon = today.AddDays(window);

        int detailsChecked = 0, detailsResolved = 0;
        var detailsRan = StartListSweepRules.DetailsDue(previousRunAt, now);
        List<StartListSweepCandidate> candidates;

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            if (detailsRan)
            {
                var discovery = scope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryService>();
                (detailsChecked, detailsResolved) = await discovery.RefreshUpcomingDetailsAsync(window, ct);
            }

            var db = scope.ServiceProvider.GetRequiredService<SwimmDbContext>();
            candidates = await LoadCandidatesAsync(db, today, horizon, window, ct);
        }

        var decisions = candidates
            .Select(c => (Candidate: c, Verdict: StartListSweepRules.Decide(c, now, window)))
            .Where(d => d.Verdict is not (StartListSweepVerdict.OutOfWindow or StartListSweepVerdict.WaitingLogligId))
            .ToList();
        var due = decisions.Where(d => d.Verdict == StartListSweepVerdict.Due)
            .Select(d => d.Candidate.OrgCompId).ToList();

        var pulled = 0;
        foreach (var orgCompId in due)
        {
            ct.ThrowIfCancellationRequested();

            await using var scope = _scopeFactory.CreateAsyncScope();
            var startList = scope.ServiceProvider.GetRequiredService<IStartListPullService>();
            try
            {
                var pull = await startList.PullAsync(orgCompId, ct);
                pulled++;
                _logger.LogInformation(
                    "Стартовый протокол (фон) compID={OrgCompId}: {Status}, новых {Added}, снялись {Removed}",
                    orgCompId, pull.Status, pull.Added, pull.Removed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Отмену НЕ глотаем: при остановке приложения обход обязан прекратиться сразу,
                // а не логировать по предупреждению на каждое оставшееся соревнование.
                _logger.LogWarning(ex, "Стартовый протокол (фон): забор compID={OrgCompId} не удался", orgCompId);
            }
        }

        var report = new StartListSweepReport(
            detailsChecked, detailsResolved,
            Total: decisions.Count,
            Pulled: pulled,
            Due: due.Count,
            TooEarly: decisions.Count(d => d.Verdict == StartListSweepVerdict.TooEarly),
            Started: decisions.Count(d => d.Verdict == StartListSweepVerdict.Started),
            DetailsRan: detailsRan);

        _logger.LogInformation(
            "Стартовый протокол (фон): детали {DetailsRan} — проверено {Checked}, добыто {Resolved}; " +
            "в окне {Total}, пора {Due}, рано {TooEarly}, начались {Started}; заборов {Pulled}",
            detailsRan ? "шли" : "не в этот тик", detailsChecked, detailsResolved,
            report.Total, report.Due, report.TooEarly, report.Started, pulled);

        return report;
    }

    /// <summary>
    /// Старты окна с тем, что нужно правилу: журнал заборов (только за окно — раньше посев не
    /// выкладывают) и время первого заплыва у сегодняшних. Три запроса на тик, без сети.
    /// </summary>
    private static async Task<List<StartListSweepCandidate>> LoadCandidatesAsync(
        SwimmDbContext db, DateTime today, DateTime horizon, int window, CancellationToken ct)
    {
        var rows = await db.DiscoveredCompetitions.AsNoTracking()
            .Where(d => d.Status != DiscoveredCompetitionStatus.Ignored
                        && d.DateStart >= today
                        && d.DateStart <= horizon)
            .Select(d => new { d.OrgCompId, d.DateStart, HasLogligId = d.LogligId != null })
            .ToListAsync(ct);
        if (rows.Count == 0) return [];

        var ids = rows.Select(r => r.OrgCompId).ToList();
        var pullsSince = DateTime.SpecifyKind(today.AddDays(-window - 1), DateTimeKind.Utc);
        var pulls = (await db.StartListPulls.AsNoTracking()
                .Where(p => ids.Contains(p.OrgCompId) && p.PulledAt >= pullsSince)
                .Select(p => new { p.OrgCompId, p.PulledAt, p.Status, p.Events, p.Entries, p.Added, p.Removed, p.Moved })
                .ToListAsync(ct))
            .ToLookup(p => p.OrgCompId,
                p => new StartListPullFact(p.PulledAt, p.Status, p.Events, p.Entries, p.Added, p.Removed, p.Moved));

        // Первый заплыв нужен только стартам сегодня: «до первого заплыва, потом не трогать».
        // Окно — сутки по Израилю в UTC; заплыв на чужую дату (Р86) сюда не попадёт.
        var todayIds = rows.Where(r => r.DateStart.Date == today).Select(r => r.OrgCompId).ToList();
        var firstHeats = new Dictionary<int, DateTime>();
        if (todayIds.Count > 0
            && IsraelTime.ToUtc(today) is { } dayStart
            && IsraelTime.ToUtc(today.AddDays(1)) is { } dayEnd)
        {
            var dayStartUtc = DateTime.SpecifyKind(dayStart, DateTimeKind.Utc);
            var dayEndUtc = DateTime.SpecifyKind(dayEnd, DateTimeKind.Utc);
            firstHeats = await db.CompetitionEntries.AsNoTracking()
                .Where(e => todayIds.Contains(e.OrgCompId)
                            && e.HeatStartAt >= dayStartUtc && e.HeatStartAt < dayEndUtc)
                .GroupBy(e => e.OrgCompId)
                .Select(g => new { OrgCompId = g.Key, First = g.Min(e => e.HeatStartAt) })
                .ToDictionaryAsync(x => x.OrgCompId, x => x.First!.Value, ct);
        }

        return rows.Select(r => new StartListSweepCandidate(
                r.OrgCompId, r.DateStart, r.HasLogligId, [.. pulls[r.OrgCompId]],
                firstHeats.TryGetValue(r.OrgCompId, out var first) ? first : null))
            .ToList();
    }
}
