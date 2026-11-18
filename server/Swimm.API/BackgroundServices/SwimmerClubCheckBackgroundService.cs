using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Сверка кандидатов на смену клуба пловца с loglig (Р98, §6 docs/plans/swimmer-club-change-plan.md).
/// С 18.11.2026 импорт клуб в карточке сам не меняет — заводит кандидата; применяет его эта служба,
/// когда карточка loglig показывает новый клуб. Два прохода:
/// <list type="bullet">
/// <item>СУТОЧНЫЙ — по расписанию (<see cref="JobNames.SwimmerClubCheck"/>, Sys_JobRuns): все ждущие;
/// ответ loglig моложе суток берётся из кэша кандидата;</item>
/// <item>ПОСЛЕ ИМПОРТА — импорт дёргает <see cref="ISwimmerClubCheckSignal"/>, служба просыпается и
/// сверяет только новых (без вердикта). В историю /Admin/Jobs — с триггером «import», расписание
/// не сдвигает.</item>
/// </list>
/// Управляется SwimmerClubCheckEnabled / SwimmerClubCheckIntervalHours (Admin/Settings). Работает
/// там же, где остальные расписания (ProdAdmin): там идёт и импорт.
/// </summary>
public sealed class SwimmerClubCheckBackgroundService(
    IServiceScopeFactory scopeFactory,
    ISwimmerClubCheckSignal signal,
    ILogger<SwimmerClubCheckBackgroundService> logger) : BackgroundService
{
    /// <summary>Как часто перечитываем настройки/проверяем, не пора ли бежать (если сигнала нет).</summary>
    private static readonly TimeSpan IdleCheck = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Пауза после сигнала: импорт многодневки идёт днями подряд, и каждый день шлёт сигнал —
    /// даём им собраться в один проход.
    /// </summary>
    private static readonly TimeSpan SignalSettle = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var afterImport = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                var job = JobNames.Get(JobNames.SwimmerClubCheck);
                if (job.IsEnabled(settings))
                {
                    if (afterImport) await RunAfterImportAsync(stoppingToken);

                    await ScheduledJob.RunIfDueAsync(scopeFactory, JobNames.SwimmerClubCheck, job.Interval(settings), async ct =>
                    {
                        await using var work = scopeFactory.CreateAsyncScope();
                        var report = await work.ServiceProvider.GetRequiredService<ISwimmerClubLogligCheckService>()
                            .CheckPendingAsync(SwimmerClubCheckScope.AllPending, ct);
                        var summary = JobRunSummaries.SwimmerClubCheck(report);
                        logger.LogInformation("Клуб пловца ↔ loglig (фон): {Summary}", summary);
                        return summary;
                    }, logger, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Сюда — только сбой журнала/настроек (база лежит): ошибки суточного прогона ловит ScheduledJob.
                logger.LogWarning(ex, "Клуб пловца ↔ loglig (фон): тик не удался");
            }

            try
            {
                afterImport = await signal.WaitAsync(IdleCheck, stoppingToken);
                if (afterImport) await Task.Delay(SignalSettle, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break; // остановка сервиса — штатный выход
            }
        }
    }

    /// <summary>Проход после импорта: только новые кандидаты; итог — в историю (триггер import).</summary>
    private async Task RunAfterImportAsync(CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        string? summary = null, error = null;
        try
        {
            await using var work = scopeFactory.CreateAsyncScope();
            var report = await work.ServiceProvider.GetRequiredService<ISwimmerClubLogligCheckService>()
                .CheckPendingAsync(SwimmerClubCheckScope.Unchecked, ct);
            if (report.Checked == 0) return; // нечего сверять — историю не засоряем
            summary = JobRunSummaries.SwimmerClubCheck(report);
            logger.LogInformation("Клуб пловца ↔ loglig (после импорта): {Summary}", summary);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Клуб пловца ↔ loglig (после импорта) не удался; догонит суточный прогон");
            error = ex.Message;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IJobRunStore>().AppendLogAsync(
            new JobRunRecord(JobNames.SwimmerClubCheck, JobTriggers.Import, startedAt, DateTime.UtcNow,
                Success: error is null, summary, error), ct);
    }
}
