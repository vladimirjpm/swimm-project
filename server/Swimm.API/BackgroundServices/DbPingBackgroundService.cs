using Swimm.API.Services;
using Swimm.Infrastructure.Data;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Фоновый сервис: раз в 30 секунд пингует БД через CanConnectAsync
/// и обновляет <see cref="DbStatusService"/>.
/// </summary>
public sealed class DbPingBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DbStatusService _dbStatus;
    private readonly ILogger<DbPingBackgroundService> _logger;

    public DbPingBackgroundService(
        IServiceScopeFactory scopeFactory,
        DbStatusService dbStatus,
        ILogger<DbPingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _dbStatus = dbStatus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Первый пинг — немедленно при старте приложения
        await PingAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            await PingAsync(stoppingToken);
        }
    }

    private async Task PingAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SwimmDbContext>();
            var ok = await db.Database.CanConnectAsync(ct);
            if (ok)
                _dbStatus.MarkAvailable();
            else
                _dbStatus.MarkUnavailable();
        }
        catch (OperationCanceledException)
        {
            // приложение останавливается — не логируем
        }
        catch (Exception ex)
        {
            _dbStatus.MarkUnavailable();
            _logger.LogWarning(ex, "DB ping завершился ошибкой — БД недоступна.");
        }
    }
}
