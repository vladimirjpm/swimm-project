using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;

namespace Swimm.API.BackgroundServices;

/// <summary>
/// Ночная верификация краудсорс-предложений Loglig ID (docs/loglig-id-plan.md, шаг 6):
/// раз в LogligVerifyIntervalHours берёт все Suggested-привязки, сверяет карточку loglig
/// (имя + год рождения; спорные — полной сверкой результатов) → Verified / Rejected.
/// Управляется настройками LogligVerifyEnabled / LogligVerifyIntervalHours (Admin/Settings,
/// проверяются каждый тик). Включено по умолчанию: без джоба предложения пользователей
/// зависали бы в Suggested навсегда. «Последний запуск» — в журнале Sys_JobRuns (В12,
/// <see cref="ScheduledJob"/>); работает только при включённых фоновых расписаниях (ProdAdmin).
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
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();

                if (settings.GetValue("LogligVerifyEnabled", true))
                {
                    var interval = TimeSpan.FromHours(Math.Max(1, settings.GetValue("LogligVerifyIntervalHours", 24)));
                    // «Последний запуск» — в журнале Sys_JobRuns (В12): рестарт API не гонит сверку заново.
                    await ScheduledJob.RunIfDueAsync(_scopeFactory, JobNames.LogligVerify, interval, async ct =>
                    {
                        var suggestions = scope.ServiceProvider.GetRequiredService<ILogligSuggestionService>();
                        var report = await suggestions.VerifySuggestedAsync(ct);
                        if (report.Checked > 0)
                            _logger.LogInformation(
                                "Loglig verify (фон): проверено {Checked}, подтверждено {Verified}, отклонено {Rejected}, отложено {Skipped}",
                                report.Checked, report.Verified, report.Rejected, report.Skipped);
                    }, _logger, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // приложение останавливается
            }
            catch (Exception ex)
            {
                // Сюда — только сбой журнала/настроек (база лежит): ошибки сверки ловит ScheduledJob.
                _logger.LogWarning(ex, "Loglig verify (фон): тик не удался");
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
