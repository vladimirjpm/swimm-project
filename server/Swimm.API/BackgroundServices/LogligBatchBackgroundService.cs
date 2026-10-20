using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Батч-привязка Loglig ID (docs/loglig-id-plan.md, шаг 7): по расписанию прогоняет до
/// LogligBatchPerRun непривязанных пловцов через поиск+сверку (LogligBatchIntervalHours между
/// прогонами). Управляется настройками Admin/Settings, проверяются каждый тик. ВЫКЛЮЧЕНО по
/// умолчанию: каждый пловец — 1–3 платных запроса Serper, включать осознанно
/// (LogligBatchEnabled = true; 50 пловцов за прогон ≈ до 150 запросов ≈ копейки).
/// «Последний запуск» — в журнале Sys_JobRuns (В12, <see cref="ScheduledJob"/>).
/// </summary>
public sealed class LogligBatchBackgroundService : BackgroundService
{
    /// <summary>Как часто перечитываем настройки/проверяем, не пора ли бежать.</summary>
    private static readonly TimeSpan IdleCheck = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LogligBatchBackgroundService> _logger;

    public LogligBatchBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<LogligBatchBackgroundService> logger)
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

                var job = JobNames.Get(JobNames.LogligBatch);
                if (job.IsEnabled(settings))
                {
                    // «Последний запуск» — в журнале Sys_JobRuns (В12): рестарт API не тратит Serper заново.
                    await ScheduledJob.RunIfDueAsync(_scopeFactory, JobNames.LogligBatch, job.Interval(settings), async ct =>
                    {
                        var perRun = Math.Clamp(settings.GetValue("LogligBatchPerRun", 50), 1, 500);
                        var linkService = scope.ServiceProvider.GetRequiredService<ILogligLinkService>();
                        var report = await linkService.RunBatchAsync(perRun, ct);
                        var summary = JobRunSummaries.LogligBatch(report, perRun);
                        _logger.LogInformation("Loglig batch (фон): {Summary}", summary);
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
                // Сюда — только сбой журнала/настроек (база лежит): ошибки прогона ловит ScheduledJob.
                _logger.LogWarning(ex, "Loglig batch (фон): тик не удался");
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
