using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Чтение телеметрии прод-сайта из рабочей области Log Analytics (docs/plans/application-insights-plan.md,
/// Б1) — для страницы /Admin/AppInsights и блока дашборда. Читает под входом <c>az</c> Влада, без
/// секретов; админка живёт только на его машине (В17). Только чтение: в Azure ничего не пишет.
/// Сбой Azure не бросает — возвращает состояние подключения с текстом (<see cref="AppInsightsStates"/>).
/// </summary>
public interface IAppInsightsReader
{
    /// <summary>Плитки и таблицы страницы за период для роли. Удачный ответ кэшируется на 2 минуты.</summary>
    /// <param name="range">Период (<see cref="AppInsightsRanges"/>: 1 ч, 24 ч, 7 дней).</param>
    /// <param name="role">web | prodadmin (<see cref="AppInsightsRoles"/>).</param>
    /// <param name="refresh">Мимо кэша — кнопка «Обновить» страницы.</param>
    Task<AppInsightsOverview> GetOverviewAsync(TimeSpan range, string role, bool refresh = false, CancellationToken ct = default);

    /// <summary>Сводка сайта за 24 ч для блока дашборда — один дешёвый запрос, кэш 2 минуты.</summary>
    Task<AppInsightsSummary> GetSummaryAsync(CancellationToken ct = default);
}
