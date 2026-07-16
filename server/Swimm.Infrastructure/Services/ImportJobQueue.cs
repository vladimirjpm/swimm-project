using System.Collections.Concurrent;
using System.Threading.Channels;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// In-memory очередь задач импорта. Singleton — живёт всё время процесса.
/// BackgroundService читает из канала и обновляет статусы через <see cref="SetRunning"/>,
/// <see cref="SetCompleted"/> и <see cref="SetFailed"/>.
/// </summary>
public sealed class ImportJobQueue : IImportJobQueue
{
    internal record ImportJobItem(Guid JobId, byte[] Data, string FileName, IReadOnlyList<string> CategoryKeys, ImportEventOptions? EventOptions, int? DiscoveredId = null);

    private readonly Channel<ImportJobItem> _channel =
        Channel.CreateBounded<ImportJobItem>(new BoundedChannelOptions(20)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    private readonly ConcurrentDictionary<Guid, ImportJobStatus> _statuses = new();

    public Guid Enqueue(byte[] data, string fileName, IReadOnlyList<string>? categoryKeys = null, ImportEventOptions? eventOptions = null, int? discoveredId = null)
    {
        var jobId = Guid.NewGuid();
        var status = new ImportJobStatus
        {
            JobId = jobId,
            State = ImportJobState.Queued,
            QueuedAt = DateTimeOffset.UtcNow
        };
        _statuses[jobId] = status;
        // TryWrite всегда успешен при BoundedChannelFullMode.Wait + sync caller,
        // но если канал полон — возвращаем статус Queued, фоновый процесс заберёт позже.
        _channel.Writer.TryWrite(new ImportJobItem(jobId, data, fileName, categoryKeys ?? [], eventOptions, discoveredId));
        return jobId;
    }

    public ImportJobStatus? GetStatus(Guid jobId) =>
        _statuses.TryGetValue(jobId, out var s) ? s : null;

    // Вызывается только из ImportBackgroundService:

    public async IAsyncEnumerable<(Guid JobId, byte[] Data, string FileName, IReadOnlyList<string> CategoryKeys, ImportEventOptions? EventOptions, int? DiscoveredId)> ConsumeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(ct))
            yield return (item.JobId, item.Data, item.FileName, item.CategoryKeys, item.EventOptions, item.DiscoveredId);
    }

    public void SetRunning(Guid jobId)
    {
        if (_statuses.TryGetValue(jobId, out var s))
            s.State = ImportJobState.Running;
    }

    public void SetCompleted(Guid jobId, ImportResult result)
    {
        if (_statuses.TryGetValue(jobId, out var s))
        {
            s.State = ImportJobState.Completed;
            s.Result = result;
            s.CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    public void SetFailed(Guid jobId, string error)
    {
        if (_statuses.TryGetValue(jobId, out var s))
        {
            s.State = ImportJobState.Failed;
            s.Error = error;
            s.CompletedAt = DateTimeOffset.UtcNow;
        }
    }
}
