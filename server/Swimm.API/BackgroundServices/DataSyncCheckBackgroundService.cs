using Swimm.Application.Abstractions;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Напоминание «локальная база отстаёт от прода» (решение В17, docs/plans/azure-deploy-plan.md
/// §0-тер-бис). Только в разработке и только с заданной ProdVersionConnection: сверяет вскоре
/// после старта и потом раз в 15 минут; в лог — Warning, когда отставание появилось или выросло
/// (не каждый тик). Жёлтая полоса в админке читает то же состояние.
///
/// Старт API не ждёт сверки: прод может быть недоступен (файрвол Azure, нет сети), а локальная
/// работа от этого страдать не должна.
/// </summary>
public sealed class DataSyncCheckBackgroundService(
    IDataSyncStatus status,
    ILogger<DataSyncCheckBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!status.Enabled) return;

        long? warnedAt = null;
        var errorLogged = false;
        try
        {
            await Task.Delay(FirstCheckDelay, ct);
            while (!ct.IsCancellationRequested)
            {
                var state = await status.CheckAsync(ct);
                if (state?.Warning() is { } warning && state.ProdVersion != warnedAt)
                {
                    logger.LogWarning("{Warning}", warning);
                    warnedAt = state.ProdVersion;
                }
                if (state?.Error is { } error && !errorLogged)
                {
                    logger.LogInformation("Сверка с продом не удалась ({Error}) — локальная работа не затронута.", error);
                    errorLogged = true;
                }
                await Task.Delay(Interval, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // приложение останавливается
        }
    }
}
