using Swimm.Application.Abstractions;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Фоновая проверка isr.org.il (B3, фаза 6): по расписанию синхронизирует «входящие»
/// Sys_DiscoveredCompetitions. Управляется настройками DiscoveryEnabled /
/// DiscoveryIntervalHours (Admin/Settings, in-memory) — проверяются каждый тик,
/// перезапуск не нужен. Выключено по умолчанию.
/// </summary>
public sealed class CompetitionDiscoveryBackgroundService : BackgroundService
{
    /// <summary>Как часто перечитываем настройки в выключенном состоянии.</summary>
    private static readonly TimeSpan IdleCheck = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CompetitionDiscoveryBackgroundService> _logger;

    public CompetitionDiscoveryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<CompetitionDiscoveryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastRun = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();

                if (settings.GetValue("DiscoveryEnabled", false))
                {
                    var intervalHours = Math.Max(1, settings.GetValue("DiscoveryIntervalHours", 12));
                    if (DateTime.UtcNow - lastRun >= TimeSpan.FromHours(intervalHours))
                    {
                        var discovery = scope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryService>();
                        var result = await discovery.SyncAsync(year: null, stoppingToken); // фон тянет только текущий сезон
                        lastRun = DateTime.UtcNow;
                        _logger.LogInformation(
                            "Discovery (фон): на сайте {Total}, новых {Added}", result.TotalOnSite, result.Added);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // приложение останавливается
            }
            catch (Exception ex)
            {
                // Ошибка сети/вёрстки не должна убить сервис — увидим в логах и на странице Discovery.
                _logger.LogWarning(ex, "Discovery (фон): синхронизация не удалась");
            }

            try
            {
                await Task.Delay(IdleCheck, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break; // остановка сервиса — штатный выход
            }
        }
    }
}
