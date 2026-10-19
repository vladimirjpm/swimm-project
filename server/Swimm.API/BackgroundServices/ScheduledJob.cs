using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Общий шаг фоновых РАСПИСАНИЙ (В12, docs/plans/competitions-home-plan.md, часть В): спросить
/// журнал <c>Sys_JobRuns</c>, пора ли (<see cref="JobSchedule.IsDue"/>), выполнить и записать итог.
/// «Последний запуск» в базе, а не в памяти — перезапуск API задачу заново не запускает.
///
/// Журнал читается и пишется в СВОЁМ скоупе (свой DbContext): упавшая работа могла оставить в
/// контексте недописанные изменения, и запись «неудача» не должна их сохранить заодно.
/// </summary>
internal static class ScheduledJob
{
    /// <summary>
    /// Выполнить <paramref name="work"/>, если задаче <paramref name="name"/> пора. Ошибка работы
    /// не летит наружу: пишется в журнал (повтор — через <see cref="JobSchedule.RetryAfterFailure"/>)
    /// и в лог. Отмена (остановка приложения) летит — её не записываем.
    /// </summary>
    /// <returns>true — работа запускалась (успешно или нет).</returns>
    public static async Task<bool> RunIfDueAsync(
        IServiceScopeFactory scopeFactory, string name, TimeSpan interval,
        Func<CancellationToken, Task> work, ILogger logger, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var state = await scope.ServiceProvider.GetRequiredService<IJobRunStore>().GetAsync(name, ct);
            if (!JobSchedule.IsDue(now, state.LastSuccessAt, state.LastAttemptAt, interval, JobSchedule.RetryAfterFailure))
                return false;
        }

        string? error = null;
        try
        {
            await work(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Ошибка сети/вёрстки не должна убить сервис — в лог и в журнал, повтор через час.
            logger.LogWarning(ex, "Фоновая задача {Job} не удалась; повтор не раньше чем через {Retry}",
                name, JobSchedule.RetryAfterFailure);
            error = ex.Message;
        }

        await using (var scope = scopeFactory.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IJobRunStore>()
                .RecordAsync(name, now, success: error is null, error, ct);
        return true;
    }
}
