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
    internal record ImportJobItem(Guid JobId, byte[] Data, string FileName, IReadOnlyList<string> CategoryKeys, ImportEventOptions? EventOptions, int? DiscoveredId = null, int? OrgCompId = null);

    // Без верхней границы: Enqueue синхронный и пишет TryWrite — у ограниченного канала (было 20)
    // лишние задачи молча НЕ попадали в очередь и навсегда оставались «Queued», а пакетный импорт
    // отправляет до 30 строк за раз и после отправки удаляет их разборы (08.10.2026). Задача — это
    // JSON разбора одного протокола; держать их в памяти дешевле, чем терять.
    private readonly Channel<ImportJobItem> _channel =
        Channel.CreateUnbounded<ImportJobItem>(new UnboundedChannelOptions
        {
            SingleReader = true
        });

    private readonly ConcurrentDictionary<Guid, ImportJobStatus> _statuses = new();

    public Guid Enqueue(byte[] data, string fileName, IReadOnlyList<string>? categoryKeys = null, ImportEventOptions? eventOptions = null, int? discoveredId = null, int? orgCompId = null)
    {
        var jobId = Guid.NewGuid();
        var status = new ImportJobStatus
        {
            JobId = jobId,
            State = ImportJobState.Queued,
            QueuedAt = DateTimeOffset.UtcNow
        };
        _statuses[jobId] = status;
        // У неограниченного канала TryWrite отказывает только после закрытия писателя (его никто
        // не закрывает). Отказ всё же не молчит: задача, не попавшая в канал, — Failed, а не
        // вечное Queued, которое панели пришлось бы ждать бесконечно.
        if (!_channel.Writer.TryWrite(new ImportJobItem(jobId, data, fileName, categoryKeys ?? [], eventOptions, discoveredId, orgCompId)))
        {
            status.State = ImportJobState.Failed;
            status.Error = "Очередь импорта закрыта — задача не принята";
            status.CompletedAt = DateTimeOffset.UtcNow;
        }
        return jobId;
    }

    public ImportJobStatus? GetStatus(Guid jobId) =>
        _statuses.TryGetValue(jobId, out var s) ? s : null;

    // Вызывается только из ImportBackgroundService:

    public async IAsyncEnumerable<(Guid JobId, byte[] Data, string FileName, IReadOnlyList<string> CategoryKeys, ImportEventOptions? EventOptions, int? DiscoveredId, int? OrgCompId)> ConsumeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(ct))
            yield return (item.JobId, item.Data, item.FileName, item.CategoryKeys, item.EventOptions, item.DiscoveredId, item.OrgCompId);
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
