using Swimm.Application.Abstractions;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Фоновая проверка isr.org.il (B3, фаза 6): по расписанию синхронизирует «входящие»
/// Sys_DiscoveredCompetitions. Управляется настройками DiscoveryEnabled /
/// DiscoveryIntervalHours (Admin/Settings, in-memory) — проверяются каждый тик,
/// перезапуск не нужен. Выключено по умолчанию.
///
/// С10 (docs/plans/start-list-plan.md): второй проход в том же цикле, отдельного сервиса
/// не заводим. Управляется StartListEnabled / StartListDaysAhead, тоже выключен по
/// умолчанию. Забор одного соревнования — под две сотни запросов с паузой (минуты), это
/// фон — торопиться некуда, но каждая итерация берёт свой DbContext-скоуп, не держит его
/// на всё окно, и уважает stoppingToken.
/// </summary>
public sealed class CompetitionDiscoveryBackgroundService : BackgroundService
{
    /// <summary>Как часто перечитываем настройки в выключенном состоянии.</summary>
    private static readonly TimeSpan IdleCheck = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CompetitionDiscoveryBackgroundService> _logger;

    public CompetitionDiscoveryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<CompetitionDiscoveryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastRun = DateTime.MinValue;
        var lastStartListRun = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();

                if (settings.GetValue("DiscoveryEnabled", false))
                {
                    var intervalHours = Math.Max(1, settings.GetValue("DiscoveryIntervalHours", 12));
                    if (DateTime.UtcNow - lastRun >= TimeSpan.FromHours(intervalHours))
                    {
                        var discovery = scope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryService>();
                        var result = await discovery.SyncAsync(year: null, stoppingToken); // фон тянет только текущий сезон
                        lastRun = DateTime.UtcNow;
                        _logger.LogInformation(
                            "Discovery (фон): на сайте {Total}, новых {Added}", result.TotalOnSite, result.Added);
                    }
                }

                // С10: второй проход, стартовый протокол будущих стартов. Своя настройка
                // «включено», интервал — тот же DiscoveryIntervalHours (отдельного не заводим,
                // решение 5: меньше движущихся частей). Забор одного соревнования занимает
                // минуты, поэтому идёт следом, а не параллельно с discovery.SyncAsync выше.
                if (settings.GetValue("StartListEnabled", false))
                {
                    var intervalHours = Math.Max(1, settings.GetValue("DiscoveryIntervalHours", 12));
                    if (DateTime.UtcNow - lastStartListRun >= TimeSpan.FromHours(intervalHours))
                    {
                        var daysAhead = Math.Max(1, settings.GetValue("StartListDaysAhead", 14));
                        var sweep = scope.ServiceProvider.GetRequiredService<IStartListScheduleService>();
                        var sweepReport = await sweep.RunAsync(daysAhead, stoppingToken);
                        _logger.LogInformation(
                            "Стартовый протокол (фон): loglig-id добыт у {Resolved}/{Checked}, заборов {Pulled}/{Total}",
                            sweepReport.DetailsResolved, sweepReport.DetailsChecked,
                            sweepReport.Pulled, sweepReport.Total);
                        lastStartListRun = DateTime.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // приложение останавливается
            }
            catch (Exception ex)
            {
                // Ошибка сети/вёрстки не должна убить сервис — увидим в логах и на странице Discovery.
                _logger.LogWarning(ex, "Discovery (фон): синхронизация не удалась");
            }

            try
            {
                await Task.Delay(IdleCheck, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break; // остановка сервиса — штатный выход
            }
        }
    }

}
