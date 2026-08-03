using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// Реестр проверок данных (docs/data-integrity.md, фаза Д3) — API страницы /Admin/Health.
/// Прогон читающий: проверки ставят диагноз, лечение остаётся отдельным осознанным действием.
/// </summary>
[ApiController]
[Route("api/admin/data-checks")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class DataChecksAdminController(IDataCheckRunner runner, IAdminAuditService audit) : ControllerBase
{
    /// <summary>Текущие находки по проверкам + история прогонов.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(new
        {
            groups = await runner.GetCurrentAsync(ct),
            history = await runner.GetHistoryAsync(10, ct)
        });

    /// <summary>Прогнать все проверки.</summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        var run = await runner.RunAllAsync("manual", ct);
        return Ok(new
        {
            run,
            groups = await runner.GetCurrentAsync(ct),
            history = await runner.GetHistoryAsync(10, ct)
        });
    }

    public sealed record AcceptRequest(string? Note);

    /// <summary>«Принято как есть» — находка неустранима, но решение должно пережить прогоны.</summary>
    [HttpPost("{id:int}/accept")]
    public async Task<IActionResult> Accept(int id, [FromBody] AcceptRequest? request, CancellationToken ct)
    {
        if (!await runner.AcceptAsync(id, request?.Note, ct))
            return NotFound(new { error = "Находка не найдена или уже закрыта" });

        await audit.LogAsync("datacheck.accept", "DataCheckFinding", id.ToString(),
            $"Находка #{id} принята как есть" + (string.IsNullOrWhiteSpace(request?.Note) ? "" : $": {request!.Note}"), ct: ct);
        return Ok();
    }

    /// <summary>Вернуть принятую находку в работу.</summary>
    [HttpPost("{id:int}/reopen")]
    public async Task<IActionResult> Reopen(int id, CancellationToken ct)
    {
        if (!await runner.ReopenAsync(id, ct))
            return NotFound(new { error = "Находка не найдена или не была принята" });

        await audit.LogAsync("datacheck.reopen", "DataCheckFinding", id.ToString(),
            $"Находка #{id} возвращена в работу", ct: ct);
        return Ok();
    }
}
