using Swimm.Application.Abstractions;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Ночная верификация краудсорс-предложений Loglig ID (docs/loglig-id-plan.md, шаг 6):
/// раз в LogligVerifyIntervalHours берёт все Suggested-привязки, сверяет карточку loglig
/// (имя + год рождения; спорные — полной сверкой результатов) → Verified / Rejected.
/// Управляется настройками LogligVerifyEnabled / LogligVerifyIntervalHours (Admin/Settings,
/// проверяются каждый тик). Включено по умолчанию: без джоба предложения пользователей
/// зависали бы в Suggested навсегда.
/// </summary>
public sealed class LogligSuggestionVerificationBackgroundService : BackgroundService
{
    /// <summary>Как часто перечитываем настройки/проверяем, не пора ли бежать.</summary>
    private static readonly TimeSpan IdleCheck = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LogligSuggestionVerificationBackgroundService> _logger;

    public LogligSuggestionVerificationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<LogligSuggestionVerificationBackgroundService> logger)
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

                if (settings.GetValue("LogligVerifyEnabled", true))
                {
                    var intervalHours = Math.Max(1, settings.GetValue("LogligVerifyIntervalHours", 24));
                    if (DateTime.UtcNow - lastRun >= TimeSpan.FromHours(intervalHours))
                    {
                        var suggestions = scope.ServiceProvider.GetRequiredService<ILogligSuggestionService>();
                        var report = await suggestions.VerifySuggestedAsync(stoppingToken);
                        lastRun = DateTime.UtcNow;
                        if (report.Checked > 0)
                            _logger.LogInformation(
                                "Loglig verify (фон): проверено {Checked}, подтверждено {Verified}, отклонено {Rejected}, отложено {Skipped}",
                                report.Checked, report.Verified, report.Rejected, report.Skipped);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // приложение останавливается
            }
            catch (Exception ex)
            {
                // Ошибка сети/вёрстки не должна убить сервис — увидим в логах.
                _logger.LogWarning(ex, "Loglig verify (фон): проверка предложений не удалась");
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
