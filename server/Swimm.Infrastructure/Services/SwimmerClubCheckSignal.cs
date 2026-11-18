using Swimm.Application.Abstractions;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// «Импорт завёл кандидатов на смену клуба» (Р98): будит фоновую сверку с loglig сразу, а не на
/// ближайшем тике. Синглтон; семафор с потолком 1 — пачка сигналов подряд сливается в один.
/// </summary>
public sealed class SwimmerClubCheckSignal : ISwimmerClubCheckSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* сигнал уже ждёт — достаточно одного */ }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}
