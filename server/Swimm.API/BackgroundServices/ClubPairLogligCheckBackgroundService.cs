using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Services;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Выполняет прогоны «Проверить в loglig» (Р112, /Admin/Clubs) из очереди: одну пару или все пары
/// эвристики «общие пловцы». Сам ход — в <see cref="IClubPairLogligService.RunAsync"/>; здесь
/// обвязка: scope, список пар, статусы и лог. Очередь (ставит админка), живёт под adminEnabled.
///
/// Фоновая, потому что сотня карточек при ~1 запросе в секунду — минуты: HTTP-запрос админки
/// столько держать нельзя. Страница опрашивает статус и по ходу перерисовывает сводки пар.
/// </summary>
public sealed class ClubPairLogligCheckBackgroundService(
    IClubPairLogligCheckQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ClubPairLogligCheckBackgroundService> logger) : BackgroundService
{
    // IClubPairLogligCheckQueue зарегистрирован как ClubPairLogligCheckQueue (singleton).
    private readonly ClubPairLogligCheckQueue _queue = (ClubPairLogligCheckQueue)queue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var runId in _queue.ConsumeAsync(stoppingToken))
            {
                var status = _queue.SetRunning(runId);
                if (status is null) continue;
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    IReadOnlyList<(int A, int B)> pairs = status.AllPairs
                        ? (await scope.ServiceProvider.GetRequiredService<IClubDedupService>().FindCandidatesAsync(stoppingToken))
                            .Candidates.Where(c => c.Heuristic == "swimmers")
                            .Select(c => (c.CanonicalId, c.DuplicateId)).ToList()
                        : [(status.ClubAId!.Value, status.ClubBId!.Value)];

                    await scope.ServiceProvider.GetRequiredService<IClubPairLogligService>().RunAsync(status, pairs, stoppingToken);
                    _queue.SetCompleted(runId);
                    logger.LogInformation(
                        "Проверка пар клубов в loglig {RunId}: пар {Pairs}, карточек {Done}/{Total}, не открылось {Failed}",
                        runId, status.PairsDone, status.SwimmersDone, status.SwimmersTotal, status.Failed);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _queue.SetFailed(runId, "Прервано остановкой сервера — сделанное сохранено, запустите заново.");
                    throw;
                }
                catch (Exception ex)
                {
                    _queue.SetFailed(runId, ex.Message);
                    logger.LogError(ex, "Проверка пар клубов в loglig {RunId} упала", runId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка хоста.
        }
    }
}
