using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Проверка источников справочника рекордов по расписанию (docs/plans/records-freshness-plan.md
/// §4): раз в RecordsCheckIntervalHours делает ровно то, что кнопка «🔎 Проверить все источники»
/// на /Admin/Import, — <see cref="IRecordSourceCheckService.CheckAllAsync"/>: скачать каждый
/// источник, построить dry-run дифф, записать строку в <c>Sys_RecordSourceChecks</c>.
///
/// Apply НЕ делает: применяет человек на /Admin/Import, джоб только освежает «проверено» и
/// «ждёт Apply» на дашборде. Упавший источник — строка <c>failed</c> в его журнале, а не упавший
/// прогон: остальные источники проверены, дашборд подсветит упавший сам.
///
/// Управляется RecordsCheckEnabled / RecordsCheckIntervalHours (Admin/Settings, читаются каждый
/// тик); работает только при включённых фоновых расписаниях (ProdAdmin) — как loglig-verify.
/// </summary>
public sealed class RecordsCheckBackgroundService : BackgroundService
{
    /// <summary>Как часто перечитываем настройки/проверяем, не пора ли бежать.</summary>
    private static readonly TimeSpan IdleCheck = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecordsCheckBackgroundService> _logger;

    public RecordsCheckBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<RecordsCheckBackgroundService> logger)
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

                var job = JobNames.Get(JobNames.RecordsCheck);
                if (job.IsEnabled(settings))
                {
                    await ScheduledJob.RunIfDueAsync(_scopeFactory, JobNames.RecordsCheck, job.Interval(settings), async ct =>
                    {
                        var checks = scope.ServiceProvider.GetRequiredService<IRecordSourceCheckService>();
                        // Упавшие источники — ещё раз через минуту (RecordsCheckRun).
                        var results = await RecordsCheckRun.RunAsync(checks, RecordsCheckRun.RetryDelay, ct);
                        var summary = JobRunSummaries.RecordsCheck(results);
                        _logger.LogInformation("Records check (фон): {Summary}", summary);
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
                // Сюда — только сбой журнала/настроек (база лежит): ошибки проверки ловит ScheduledJob.
                _logger.LogWarning(ex, "Records check (фон): тик не удался");
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
