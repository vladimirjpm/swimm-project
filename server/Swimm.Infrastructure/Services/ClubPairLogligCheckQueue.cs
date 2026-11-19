using System.Collections.Concurrent;
using System.Threading.Channels;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// In-memory очередь «Проверить в loglig» (Р112) — по образцу <see cref="RecordCountryRunQueue"/>.
/// Singleton на весь процесс. Активный прогон один: постановка под замком проверяет, нет ли уже
/// ждущего или идущего, и отдаёт его вместо нового.
/// </summary>
public sealed class ClubPairLogligCheckQueue : IClubPairLogligCheckQueue
{
    /// <summary>Сколько завершённых прогонов помнить (их видит /Admin/Jobs).</summary>
    private const int KeepFinished = 20;

    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, ClubPairLogligRunStatus> _statuses = new();
    private readonly Lock _gate = new();

    public (ClubPairLogligRunStatus Status, bool Started) Enqueue(int? clubA, int? clubB)
    {
        lock (_gate)
        {
            if (_statuses.Values.FirstOrDefault(s => s.Active) is { } active) return (active, false);

            var all = clubA is null || clubB is null;
            var status = new ClubPairLogligRunStatus
            {
                RunId = Guid.NewGuid(),
                AllPairs = all,
                ClubAId = all ? null : clubA,
                ClubBId = all ? null : clubB,
                QueuedAt = DateTimeOffset.UtcNow,
            };
            _statuses[status.RunId] = status;
            Trim();
            _channel.Writer.TryWrite(status.RunId); // безразмерный канал: запись не отказывает
            return (status, true);
        }
    }

    public ClubPairLogligRunStatus? GetStatus(Guid runId) => _statuses.GetValueOrDefault(runId);

    public ClubPairLogligRunStatus? Current() =>
        _statuses.Values.FirstOrDefault(s => s.Active) ?? _statuses.Values.MaxBy(s => s.QueuedAt);

    public IReadOnlyList<ClubPairLogligRunStatus> Snapshot() => [.. _statuses.Values.OrderBy(s => s.QueuedAt)];

    // Ниже — только для фоновой задачи:

    public IAsyncEnumerable<Guid> ConsumeAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

    public ClubPairLogligRunStatus? SetRunning(Guid runId)
    {
        if (!_statuses.TryGetValue(runId, out var s)) return null;
        s.StartedAt = DateTimeOffset.UtcNow;
        s.State = ClubPairLogligRunStates.Running;
        return s;
    }

    public void SetCompleted(Guid runId) => Finish(runId, ClubPairLogligRunStates.Completed, null);

    public void SetFailed(Guid runId, string error) => Finish(runId, ClubPairLogligRunStates.Failed, error);

    private void Finish(Guid runId, string state, string? error)
    {
        if (!_statuses.TryGetValue(runId, out var s)) return;
        s.CurrentClubAId = s.CurrentClubBId = null;
        s.Error = error;
        s.CompletedAt = DateTimeOffset.UtcNow;
        s.State = state;
    }

    private void Trim()
    {
        foreach (var old in _statuses.Values.Where(s => !s.Active).OrderByDescending(s => s.QueuedAt).Skip(KeepFinished))
            _statuses.TryRemove(old.RunId, out _);
    }
}
