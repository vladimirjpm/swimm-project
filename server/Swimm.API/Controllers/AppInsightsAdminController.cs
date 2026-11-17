using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Controllers;

/// <summary>
/// Телеметрия прод-сайта из Application Insights для админки (docs/plans/application-insights-plan.md,
/// Б1): страница /Admin/AppInsights и блок 10 дашборда. Только чтение, под входом az Влада.
/// Состояния «не настроено / нет входа / не прочитано» приходят в ответе 200 карточкой
/// (<see cref="AppInsightsConnection"/>), а не ошибкой. На проде админки нет — путь /api/admin даёт 404.
/// </summary>
[ApiController]
[Route("api/admin/appinsights")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class AppInsightsAdminController(IAppInsightsReader reader) : ControllerBase
{
    /// <summary>Плитки и таблицы страницы. <c>range</c> — 1h | 24h | 7d (умолчание 24h), <c>role</c> — web | prodadmin (умолчание web).</summary>
    /// <param name="refresh">true — мимо двухминутного кэша (кнопка «Обновить»).</param>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(
        [FromQuery] string? range, [FromQuery] string? role, [FromQuery] bool refresh, CancellationToken ct)
    {
        if (AppInsightsRanges.Parse(range) is not { } span)
            return BadRequest(new { error = "range — 1h, 24h или 7d" });
        if (AppInsightsRoles.Normalize(role) is not { } roleKey)
            return BadRequest(new { error = "role — web или prodadmin" });
        return Ok(await reader.GetOverviewAsync(span, roleKey, refresh, ct));
    }

    /// <summary>Сводка сайта за 24 ч для блока дашборда.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct) => Ok(await reader.GetSummaryAsync(ct));
}
