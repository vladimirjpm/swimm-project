using Swimm.Application.Abstractions;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Infrastructure.Services;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Выполняет прогоны «рекорды по странам» (11.1.2) из очереди. Сам ход прогона —
/// в <see cref="RecordCountryRunner"/>; здесь только обвязка: scope под scoped-сервисы
/// (дифф ходит в БД), перевод статусов и лог.
///
/// Прогон по всем странам — это 470 файлов и час-два работы, поэтому он и фоновый:
/// в HTTP-запрос админки такое не пролезет.
/// </summary>
public sealed class RecordCountryRunBackgroundService : BackgroundService
{
    private readonly RecordCountryRunQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecordCountryRunBackgroundService> _logger;

    public RecordCountryRunBackgroundService(
        IRecordCountryRunQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<RecordCountryRunBackgroundService> logger)
    {
        // IRecordCountryRunQueue зарегистрирован как RecordCountryRunQueue (singleton).
        _queue = (RecordCountryRunQueue)queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var (runId, codes) in _queue.ConsumeAsync(stoppingToken))
            {
                var status = _queue.SetRunning(runId);
                if (status is null) continue;   // статус вычистили — прогон уже никому не нужен

                _logger.LogInformation("Records country run {RunId} started: {Codes}",
                    runId, codes is { Count: > 0 } ? string.Join(",", codes) : "все страны");

                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var runner = scope.ServiceProvider.GetRequiredService<RecordCountryRunner>();

                    await runner.RunAsync(status, codes, stoppingToken);

                    // Журнал свежести: прогон по странам — СВОЙ тип проверки
                    // (`worldrecords-countries`), а не отчёт мировых рекордов. Без этой строки
                    // витрина подписывала страновые NR датой сверки WR-отчёта, то есть врала о
                    // свежести (замечание Влада 23.09.2026).
                    //
                    // ⚠ Только ПОЛНЫЙ прогон. Прогон подмножеством стран — отладка, он и в
                    // архив не пишется (pre-push-rules §11-бис), и в журнале сдвинул бы дату
                    // так, будто сверили все 215.
                    if (codes is not { Count: > 0 })
                    {
                        var checks = scope.ServiceProvider.GetRequiredService<IRecordSourceCheckService>();
                        await checks.LogRunAsync(
                            RecordSources.WorldRecordsCountries, status.Diff, null, stoppingToken);
                    }

                    _queue.SetCompleted(runId);
                    _logger.LogInformation(
                        "Records country run {RunId} completed: {Done}/{Total} стран, {Rows} строк, "
                        + "упало {Failed}, дифф {DiffId} (+{Added}/~{Changed})",
                        runId, status.Done, status.Total, status.RowsFetched, status.Failed.Count,
                        status.Diff?.DiffId, status.Diff?.AddedCount, status.Diff?.ChangedCount);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Гасят процесс: прогон теряется, это принято планом (11.1.2).
                    _queue.SetFailed(runId, "Прогон прерван остановкой сервера — запустите заново.");
                    throw;
                }
                catch (Exception ex)
                {
                    _queue.SetFailed(runId, ex.Message);
                    _logger.LogError(ex, "Records country run {RunId} failed", runId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка хоста.
        }
    }
}
