using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// Deep-link выборки «здоровье данных» для /Admin/Results (T3b,
/// docs/tasks/dashboard-deeplinks-lists-sonnet.md): секция «Аномалии» — read-only, без мутаций.
/// </summary>
[ApiController]
[Route("api/admin/results")]
[Authorize(Roles = "Admin")]
public class ResultsAdminController : ControllerBase
{
    private readonly IDataQualityService _quality;

    public ResultsAdminController(IDataQualityService quality) => _quality = quality;

    /// <summary>FK-аномалии (несуществующий SwimmerId/ClubId) + эстафеты без участников, топ-200 каждая.</summary>
    [HttpGet("anomalies")]
    public async Task<IActionResult> GetAnomalies(CancellationToken ct)
        => Ok(await _quality.GetResultAnomaliesAsync(ct));
}
