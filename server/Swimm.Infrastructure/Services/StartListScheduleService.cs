using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Плановый обход стартовых протоколов (docs/plans/start-list-plan.md, шаг С10).
///
/// Порядок: сперва дочитать <c>logligId</c> будущим стартам, у которых его нет (С2), затем
/// затянуть стартовый протокол у каждого будущего старта, где <c>logligId</c> уже есть.
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

    public async Task<StartListSweepReport> RunAsync(int daysAhead, CancellationToken ct = default)
    {
        var window = Math.Max(1, daysAhead);
        var today = DateTime.UtcNow.Date;
        var horizon = today.AddDays(window);

        int detailsChecked, detailsResolved;
        List<int> orgCompIds;

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var discovery = scope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryService>();
            (detailsChecked, detailsResolved) = await discovery.RefreshUpcomingDetailsAsync(window, ct);

            var db = scope.ServiceProvider.GetRequiredService<SwimmDbContext>();
            orgCompIds = await db.DiscoveredCompetitions.AsNoTracking()
                .Where(d => d.LogligId != null
                            && d.Status != DiscoveredCompetitionStatus.Ignored
                            && d.DateStart >= today
                            && d.DateStart <= horizon)
                .Select(d => d.OrgCompId)
                .ToListAsync(ct);
        }

        var pulled = 0;
        foreach (var orgCompId in orgCompIds)
        {
            ct.ThrowIfCancellationRequested();

            await using var scope = _scopeFactory.CreateAsyncScope();
            var startList = scope.ServiceProvider.GetRequiredService<IStartListPullService>();
            try
            {
                var report = await startList.PullAsync(orgCompId, ct);
                pulled++;
                _logger.LogInformation(
                    "Стартовый протокол (фон) compID={OrgCompId}: {Status}, новых {Added}, снялись {Removed}",
                    orgCompId, report.Status, report.Added, report.Removed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Отмену НЕ глотаем: при остановке приложения обход обязан прекратиться сразу,
                // а не логировать по предупреждению на каждое оставшееся соревнование.
                _logger.LogWarning(ex, "Стартовый протокол (фон): забор compID={OrgCompId} не удался", orgCompId);
            }
        }

        _logger.LogInformation(
            "Стартовый протокол (фон): детали — проверено {Checked}, добыто {Resolved}; заборов {Pulled}/{Total}",
            detailsChecked, detailsResolved, pulled, orgCompIds.Count);

        return new StartListSweepReport(detailsChecked, detailsResolved, orgCompIds.Count, pulled);
    }
}
