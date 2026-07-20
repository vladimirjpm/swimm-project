using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// Сводка «Статус данных» для дашборда /Admin (docs/plans/admin-dashboard-status-cards-plan.md):
/// дубли пловцов/клубов, привязка Loglig ID, разбор Discovery — одним запросом.
/// </summary>
[ApiController]
[Route("api/admin/dashboard")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class DashboardAdminController : ControllerBase
{
    private readonly IDashboardStatusService _status;

    public DashboardAdminController(IDashboardStatusService status)
    {
        _status = status;
    }

    /// <summary>Сводка карточек «Статус данных»: дубли, Loglig, Discovery.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
        => Ok(await _status.GetStatusAsync(ct));
}
