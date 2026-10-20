using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Фоновая проверка isr.org.il (B3, фаза 6): по расписанию синхронизирует «входящие»
/// Sys_DiscoveredCompetitions. Управляется настройками DiscoveryEnabled /
/// DiscoveryIntervalHours (Admin/Settings) — проверяются каждый тик, перезапуск не нужен.
/// Выключено по умолчанию.
///
/// «Последний запуск» — в журнале <c>Sys_JobRuns</c> (В12, <see cref="ScheduledJob"/>), задачи
/// <see cref="JobNames.Discovery"/> и <see cref="JobNames.StartList"/>: перезапуск API наружу не
/// ходит. Кнопка «Проверить сейчас» (текущий сезон) пишет в тот же журнал успех discovery.
/// Регистрируется только там, где включены фоновые расписания (<c>HostingMode.BackgroundJobsEnabled</c>:
/// ProdAdmin — да, прод и локальная разработка — нет).
///
/// С10 (docs/plans/start-list-plan.md): второй проход в том же цикле, отдельного сервиса
/// не заводим. Управляется StartListEnabled / StartListDaysAhead, тоже выключен по
/// умолчанию. Забор одного соревнования — под две сотни запросов с паузой (минуты), это
/// фон — торопиться некуда, но каждая итерация берёт свой DbContext-скоуп, не держит его
/// на всё окно, и уважает stoppingToken.
/// </summary>
public sealed class CompetitionDiscoveryBackgroundService : BackgroundService
{
    /// <summary>Как часто перечитываем настройки и журнал.</summary>
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
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                var discoveryJob = JobNames.Get(JobNames.Discovery);
                if (discoveryJob.IsEnabled(settings))
                {
                    await ScheduledJob.RunIfDueAsync(_scopeFactory, JobNames.Discovery, discoveryJob.Interval(settings), async ct =>
                    {
                        await using var jobScope = _scopeFactory.CreateAsyncScope();
                        var discovery = jobScope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryService>();
                        var result = await discovery.SyncAsync(year: null, ct); // фон тянет только текущий сезон
                        var summary = JobRunSummaries.Discovery(result);
                        _logger.LogInformation("Discovery (фон): {Summary}", summary);
                        return summary;
                    }, _logger, stoppingToken);
                }

                // С10: второй проход, стартовый протокол будущих стартов. Своя настройка
                // «включено», интервал — тот же DiscoveryIntervalHours (отдельного не заводим,
                // решение 5: меньше движущихся частей). Забор одного соревнования занимает
                // минуты, поэтому идёт следом, а не параллельно с discovery.SyncAsync выше.
                // Своя строка журнала: упавший забор списка не откладывает протоколы и наоборот.
                var startListJob = JobNames.Get(JobNames.StartList);
                if (startListJob.IsEnabled(settings))
                {
                    await ScheduledJob.RunIfDueAsync(_scopeFactory, JobNames.StartList, startListJob.Interval(settings), async ct =>
                    {
                        await using var jobScope = _scopeFactory.CreateAsyncScope();
                        var daysAhead = Math.Max(1, settings.GetValue("StartListDaysAhead", 14));
                        var sweep = jobScope.ServiceProvider.GetRequiredService<IStartListScheduleService>();
                        var sweepReport = await sweep.RunAsync(daysAhead, ct);
                        var summary = JobRunSummaries.StartList(sweepReport, daysAhead);
                        _logger.LogInformation("Стартовый протокол (фон): {Summary}", summary);
                        return summary;
                    }, _logger, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // приложение останавливается
            }
            catch (Exception ex)
            {
                // Сюда — только сбой самого журнала/настроек (база лежит): работа ловит свои ошибки сама.
                _logger.LogWarning(ex, "Discovery (фон): тик не удался");
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
