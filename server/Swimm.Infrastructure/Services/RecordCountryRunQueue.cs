using System.Collections.Concurrent;
using System.Threading.Channels;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// In-memory очередь прогонов «рекорды по странам» (11.1.2) — по образцу
/// <see cref="ImportJobQueue"/>. Singleton: живёт всё время процесса, статусы отдаются
/// ссылкой, фоновая задача правит их по ходу прогона.
///
/// Очередь на одну позицию и <c>SingleReader</c>: два прогона по 470 файлов одновременно
/// источнику не понравятся, да и дифф от них перемешался бы.
/// </summary>
public sealed class RecordCountryRunQueue : IRecordCountryRunQueue
{
    private readonly Channel<(Guid RunId, IReadOnlyList<string>? Codes)> _channel =
        Channel.CreateBounded<(Guid, IReadOnlyList<string>?)>(new BoundedChannelOptions(4)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    private readonly ConcurrentDictionary<Guid, RecordCountryRunStatus> _statuses = new();

    public Guid Enqueue(IReadOnlyList<string>? codes)
    {
        var runId = Guid.NewGuid();
        var status = new RecordCountryRunStatus
        {
            RunId = runId,
            State = RecordCountryRunState.Queued,
            QueuedAt = DateTimeOffset.UtcNow,
        };
        _statuses[runId] = status;

        // Очередь переполнена — говорим об этом сразу. Молчаливый TryWrite оставил бы прогон
        // вечно в «Queued»: админ смотрел бы на статус, который уже никто не возьмёт.
        if (!_channel.Writer.TryWrite((runId, codes)))
        {
            status.State = RecordCountryRunState.Failed;
            status.Error = "Очередь прогонов заполнена — дождитесь окончания текущего.";
            status.CompletedAt = DateTimeOffset.UtcNow;
        }

        return runId;
    }

    public RecordCountryRunStatus? GetStatus(Guid runId) =>
        _statuses.TryGetValue(runId, out var s) ? s : null;

    // Ниже — только для RecordCountryRunBackgroundService:

    public IAsyncEnumerable<(Guid RunId, IReadOnlyList<string>? Codes)> ConsumeAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    public RecordCountryRunStatus? SetRunning(Guid runId)
    {
        if (!_statuses.TryGetValue(runId, out var s)) return null;
        s.State = RecordCountryRunState.Running;
        return s;
    }

    public void SetCompleted(Guid runId)
    {
        if (!_statuses.TryGetValue(runId, out var s)) return;
        s.State = RecordCountryRunState.Completed;
        s.CurrentCode = null;
        s.CompletedAt = DateTimeOffset.UtcNow;
    }

    public void SetFailed(Guid runId, string error)
    {
        if (!_statuses.TryGetValue(runId, out var s)) return;
        s.State = RecordCountryRunState.Failed;
        s.CurrentCode = null;
        s.Error = error;
        s.CompletedAt = DateTimeOffset.UtcNow;
    }
}
