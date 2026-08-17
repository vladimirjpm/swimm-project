using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Services;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Обрабатывает очередь задач импорта JSON в фоне.
/// Singleton-lifetime через IHostedService; для IImportService (scoped) создаём scope вручную.
/// </summary>
public sealed class ImportBackgroundService : BackgroundService
{
    private readonly ImportJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportBackgroundService> _logger;

    public ImportBackgroundService(
        IImportJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ImportBackgroundService> logger)
    {
        // IImportJobQueue зарегистрирован как ImportJobQueue (singleton); кастуем безопасно.
        _queue = (ImportJobQueue)queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var (jobId, data, fileName, categoryKeys, eventOptions, discoveredId, orgCompId) in _queue.ConsumeAsync(stoppingToken))
            {
                _queue.SetRunning(jobId);
                _logger.LogInformation("Import job {JobId} started: {FileName}", jobId, fileName);

                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var importService = scope.ServiceProvider.GetRequiredService<IImportService>();

                    using var stream = new MemoryStream(data, writable: false);
                    var result = await importService.ImportAsync(stream, fileName, categoryKeys, eventOptions, orgCompId);

                    _queue.SetCompleted(jobId, result);
                    _logger.LogInformation("Import job {JobId} completed: {Created} created, {Errors} errors",
                        jobId, result.Created, result.Errors);

                    if (discoveredId is int id)
                    {
                        try
                        {
                            var discovery = scope.ServiceProvider.GetRequiredService<ICompetitionDiscoveryService>();
                            await discovery.SetStatusAsync(id, "imported", stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Import job {JobId}: не удалось проставить статус imported для discovery {DiscoveredId}", jobId, id);
                        }
                    }

                    // Есть ли у соревнования ОФИЦИАЛЬНЫЙ клубный зачёт (דירוג מועדונים). Знать это
                    // нужно сразу: без него наши клубные очки не с чем сверять, а с ним расхождение
                    // значимо. Сбой проверки импорт не роняет — флаг просто останется «не проверяли»,
                    // добирается прогоном `--probe-club-standings`.
                    if (orgCompId is int compId)
                    {
                        try
                        {
                            var standings = scope.ServiceProvider.GetRequiredService<IOfficialClubStandingService>();
                            var probe = await standings.ProbeAndStampAsync(compId, stoppingToken);
                            _logger.LogInformation("Import job {JobId}: клубный зачёт compID {OrgCompId} — {Message}",
                                jobId, compId, probe?.Message ?? "проверить не удалось");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Import job {JobId}: не удалось проверить клубный зачёт compID {OrgCompId}", jobId, compId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _queue.SetFailed(jobId, ex.Message);
                    _logger.LogError(ex, "Import job {JobId} failed", jobId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка хоста: ReadAllAsync бросает OperationCanceledException при отмене токена.
            // Это не ошибка — просто выходим из цикла.
        }
    }
}
