using Swimm.Application.Dtos;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Очередь импорта. Канал был ограничен 20 задачами, а Enqueue писал TryWrite без проверки:
/// 21-я и дальше молча не попадали в канал и навсегда оставались «Queued» — пакетный импорт
/// отправляет до 30 строк за раз и после отправки удаляет их разборы (08.10.2026).
/// </summary>
public class ImportJobQueueTests
{
    [Fact]
    public async Task Enqueue_MoreThanTwenty_EveryJobReachesTheConsumer()
    {
        var queue = new ImportJobQueue();
        var ids = Enumerable.Range(1, 30)
            .Select(i => queue.Enqueue([1, 2, 3], $"file-{i}.json", discoveredId: i))
            .ToList();

        Assert.All(ids, id => Assert.Equal(ImportJobState.Queued, queue.GetStatus(id)!.State));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var consumed = new List<Guid>();
        await foreach (var job in queue.ConsumeAsync(cts.Token))
        {
            consumed.Add(job.JobId);
            if (consumed.Count == ids.Count) break;
        }

        Assert.Equal(ids, consumed);
    }
}
