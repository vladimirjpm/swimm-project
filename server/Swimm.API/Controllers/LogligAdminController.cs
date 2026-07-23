using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// Админ-API привязки Loglig ID к пловцу (docs/loglig-id-plan.md, шаг 5): список пловцов,
/// автопоиск+привязка, ручная привязка, отвязка.
/// </summary>
[ApiController]
[Route("api/admin/loglig")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class LogligAdminController(ILogligLinkService linkService, ICandidateSearchProvider searchProvider) : ControllerBase
{
    /// <summary>Конфигурация для UI: доступен ли автопоиск кандидатов (задан ли CandidateSearch:ApiKey).</summary>
    [HttpGet("config")]
    public IActionResult Config() => Ok(new { searchConfigured = searchProvider.IsConfigured });

    /// <summary>Пловцы для таблицы: фильтр по имени/статусу.</summary>
    [HttpGet("list")]
    public async Task<IActionResult> List([FromQuery] string? query, [FromQuery] string? status, [FromQuery] int take, CancellationToken ct)
    {
        var effectiveTake = take <= 0 ? 50 : Math.Min(take, 200);
        return Ok(await linkService.ListAsync(query, status, effectiveTake, ct));
    }

    public sealed record FindRequest(int SwimmerId);

    /// <summary>Автопоиск кандидатов + привязка при однозначном совпадении.</summary>
    [HttpPost("find")]
    public async Task<IActionResult> Find([FromBody] FindRequest request, CancellationToken ct)
        // Error != null — это доменный ответ для UI (уже привязан, поиск не дал результата и т.п.), не HTTP-ошибка.
        => Ok(await linkService.FindAndLinkAsync(request.SwimmerId, ct));

    public sealed record SetRequest(int SwimmerId, int LogligId);

    /// <summary>Ручная привязка админом.</summary>
    [HttpPost("set")]
    public async Task<IActionResult> Set([FromBody] SetRequest request, CancellationToken ct)
        => Ok(await linkService.SetManualAsync(request.SwimmerId, request.LogligId, ct));

    public sealed record BatchRequest(int Take);

    /// <summary>Ручной запуск батча (шаг 7): до take непривязанных пловцов через поиск+сверку.
    /// Каждый пловец — 1–3 платных Serper-запроса, поэтому take жёстко ограничен.</summary>
    [HttpPost("batch")]
    public async Task<IActionResult> RunBatch([FromBody] BatchRequest request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take <= 0 ? 20 : request.Take, 1, 200);
        return Ok(await linkService.RunBatchAsync(take, ct));
    }

    public sealed record UnlinkRequest(int SwimmerId);

    /// <summary>Снять привязку.</summary>
    [HttpPost("unlink")]
    public async Task<IActionResult> Unlink([FromBody] UnlinkRequest request, CancellationToken ct)
        => await linkService.UnlinkAsync(request.SwimmerId, ct)
            ? Ok()
            : NotFound(new { error = "Привязка не найдена" });
}
