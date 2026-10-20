using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Напоминания о тренировках через Web Push (docs/plans/web-push-reminders-plan.md §7): раз в
/// <see cref="TrainingReminderRules.Tick"/> спрашивает правило, кому пора, и рассылает.
/// Регистрируется только под <c>PushReminders:Enabled</c> (<see cref="Hosting.HostingMode.PushRemindersEnabled"/>)
/// — на проде, а не в ProdAdmin: от двойной отправки защищает UNIQUE журнала доставки, флаг — нет.
///
/// Журнал Sys_JobRuns: каждый тик сдвигает «последний запуск» (карточка /Admin/Jobs видит, что
/// служба жива), а строку истории пишет только тик, который что-то отправил или упал, — иначе
/// 288 пустых прогонов в сутки вытеснили бы всю историю.
/// </summary>
public sealed class TrainingReminderBackgroundService(
    IServiceScopeFactory scopeFactory, ILogger<TrainingReminderBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Старт не в ту же секунду, что миграция кэша и пинг БД: приложению дать подняться.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TrainingReminderRules.Tick);
        do
        {
            await TickAsync(stoppingToken);
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false; // остановка сервиса — штатный выход
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        string? summary = null;
        string? error = null;
        var worth = false;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var report = await scope.ServiceProvider.GetRequiredService<ITrainingReminderService>().RunAsync(started, ct);
            summary = JobRunSummaries.TrainingReminders(report);
            worth = report.Planned > 0 || report.RemovedDevices > 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Напоминания (фон): прогон не удался; повтор на следующем тике");
            error = ex.Message;
            worth = true;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IJobRunStore>();
            var run = new JobRunRecord(JobNames.TrainingReminders, JobTriggers.Schedule, started, DateTime.UtcNow,
                Success: error is null, summary, error);
            if (worth) await store.RecordAsync(run, ct);
            else await store.RecordStateAsync(run, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // База лежит — журнал не записать; напоминания от этого не дублируются (UNIQUE).
            logger.LogWarning(ex, "Напоминания (фон): журнал запусков не записан");
        }
    }
}
