using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Общий шаг фоновых РАСПИСАНИЙ (В12, docs/plans/competitions-home-plan.md, часть В): спросить
/// журнал <c>Sys_JobRuns</c>, пора ли (<see cref="JobSchedule.IsDue"/>), выполнить и записать итог.
/// «Последний запуск» в базе, а не в памяти — перезапуск API задачу заново не запускает. Что
/// прогон сделал (строка, которую вернула работа), ложится в историю <c>Sys_JobRunLog</c> —
/// её показывает /Admin/Jobs.
///
/// ЗАМОК ПРОГОНА (П1, docs/plans/prod-auto-pull-plan.md §4.1): в одну базу могут смотреть два
/// процесса (прод и ProdAdmin Влада). «Пора ли» оба читают до работы, а попытку пишут после —
/// без замка оба начали бы. Поэтому перед работой берётся заявка (<c>ClaimedAt</c>/<c>ClaimedBy</c>,
/// один атомарный запрос), после записи итога — снимается. Не досталась — «занято», выходим.
///
/// Журнал читается и пишется в СВОЁМ скоупе (свой DbContext): упавшая работа могла оставить в
/// контексте недописанные изменения, и запись «неудача» не должна их сохранить заодно.
/// </summary>
internal static class ScheduledJob
{
    /// <summary>
    /// Выполнить <paramref name="work"/>, если задаче <paramref name="name"/> пора. Работа
    /// возвращает итог одной строкой (<see cref="JobRunSummaries"/>). Ошибка работы не летит
    /// наружу: пишется в журнал (повтор — через <see cref="JobSchedule.RetryAfterFailure"/>) и в
    /// лог. Отмена (остановка приложения) летит — её не записываем.
    /// </summary>
    /// <returns>true — работа запускалась (успешно или нет).</returns>
    public static Task<bool> RunIfDueAsync(
        IServiceScopeFactory scopeFactory, string name, TimeSpan interval,
        Func<CancellationToken, Task<string?>> work, ILogger logger, CancellationToken ct) =>
        RunIfDueAsync(scopeFactory, name, interval,
            async c => new JobRunOutcome(await work(c)), logger, ct);

    /// <summary>
    /// То же, но работа отдаёт и подробности прогона (<see cref="JobRunOutcome.Details"/>, JSON
    /// <see cref="JobRunDetails"/>) — они ложатся в <c>Sys_JobRunLog.Details</c>.
    /// </summary>
    public static async Task<bool> RunIfDueAsync(
        IServiceScopeFactory scopeFactory, string name, TimeSpan interval,
        Func<CancellationToken, Task<JobRunOutcome>> work, ILogger logger, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        string owner;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IJobRunStore>();
            if (!await IsDueAsync(store, name, interval, now, ct))
                return false;

            // Заявку берём только когда пора: тик раз в 5–15 минут не должен писать в базу впустую.
            owner = JobClaimOwner.Of(scope.ServiceProvider.GetRequiredService<IHostEnvironment>());
            var claim = await store.TryClaimAsync(name, owner, JobSchedule.ClaimStaleAfter, ct);
            if (!claim.Taken)
            {
                logger.LogInformation("Фоновая задача {Job} занята: её выполняет {Holder} с {Since:u} — пропускаем тик",
                    name, claim.HeldBy ?? "другой процесс", claim.HeldSince);
                return false;
            }
        }

        try
        {
            // Пока шли к заявке, другой процесс мог успеть отработать и снять свою: «пора ли» —
            // заново, уже под замком.
            await using (var scope = scopeFactory.CreateAsyncScope())
                if (!await IsDueAsync(scope.ServiceProvider.GetRequiredService<IJobRunStore>(), name, interval, now, ct))
                    return false;

            string? error = null;
            string? summary = null;
            string? details = null;
            try
            {
                // Исходящие запросы прогона — с меткой swimm.source=job (prod-auto-pull-plan §4.2).
                using (Swimm.API.Telemetry.JobTelemetry.Begin(name))
                    (summary, details) = await work(ct);
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
                await scope.ServiceProvider.GetRequiredService<IJobRunStore>().RecordAsync(
                    new JobRunRecord(name, JobTriggers.Schedule, now, DateTime.UtcNow,
                        Success: error is null, summary, error, details), ct);
            return true;
        }
        finally
        {
            // Снимаем ПОСЛЕ записи итога (иначе в щель между ними второй процесс увидел бы «пора»).
            // И при остановке приложения тоже — токен уже отменён, поэтому None: иначе заявка
            // висела бы до ClaimStaleAfter и задерживала следующий прогон.
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IJobRunStore>()
                    .ReleaseClaimAsync(name, owner, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Не удалось снять заявку задачи {Job}; она протухнет сама через {Stale}",
                    name, JobSchedule.ClaimStaleAfter);
            }
        }
    }

    private static async Task<bool> IsDueAsync(
        IJobRunStore store, string name, TimeSpan interval, DateTime now, CancellationToken ct)
    {
        var state = await store.GetAsync(name, ct);
        return JobSchedule.IsDue(now, state.LastSuccessAt, state.LastAttemptAt, interval, JobSchedule.RetryAfterFailure);
    }
}

/// <summary>
/// Кто держит заявку на прогон: «среда@машина#pid» — видно на /Admin/Jobs («запустил Production@…»).
/// pid — чтобы перезапущенный процесс не снял заявку, которую у его предшественника перехватили.
/// </summary>
internal static class JobClaimOwner
{
    public static string Of(IHostEnvironment environment) =>
        $"{environment.EnvironmentName}@{Environment.MachineName}#{Environment.ProcessId}";
}

/// <summary>Что вернула работа расписания: итог одной строкой и (необязательно) подробности JSON-ом.</summary>
internal sealed record JobRunOutcome(string? Summary, string? Details = null);
