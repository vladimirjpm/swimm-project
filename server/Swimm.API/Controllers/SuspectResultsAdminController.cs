using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// «Проверить качество» на странице соревнования (/Admin/Competitions): прогон проверок
/// достоверности результатов и ручные пометки.
///
/// Проверки ищут ошибки САМОГО источника — то, что парсером не лечится: протокол
/// напечатан так, как напечатан (эталон — 00:32.59 на 100 м баттерфляем в протоколе
/// Маккабиады 2026). Помеченная строка остаётся в результатах, но выпадает из детекции
/// рекордов, чтобы не «бить» национальные.
/// </summary>
[ApiController]
[Route("api/admin/suspect-results")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class SuspectResultsAdminController : ControllerBase
{
    private readonly ISuspectResultService _service;
    private readonly IAdminAuditService _audit;

    public SuspectResultsAdminController(ISuspectResultService service, IAdminAuditService audit)
    {
        _service = service;
        _audit = audit;
    }

    /// <summary>Что помечено сейчас. Скоуп — событие целиком или один день.</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? eventId, [FromQuery] int? competitionId)
    {
        if (eventId is null && competitionId is null)
            return BadRequest(new { error = "Нужен eventId или competitionId" });

        return Ok(await _service.GetFlaggedAsync(eventId, competitionId, HttpContext.RequestAborted));
    }

    /// <summary>
    /// Прогнать проверки и перезаписать автоматические пометки. Ручные сохраняются.
    /// Предпочтительно вызывать с eventId: правила «повтор дисциплины» и «пол против
    /// остальных заплывов» смотрят пловца целиком по событию, а не по одному дню.
    /// </summary>
    [HttpPost("scan")]
    public async Task<IActionResult> Scan([FromQuery] int? eventId, [FromQuery] int? competitionId)
    {
        if (eventId is null && competitionId is null)
            return BadRequest(new { error = "Нужен eventId или competitionId" });

        var result = await _service.ScanAsync(eventId, competitionId, HttpContext.RequestAborted);

        await _audit.LogAsync("result.quality-scan", "Competition",
            (eventId ?? competitionId)?.ToString(),
            $"Проверка качества: просмотрено {result.Scanned}, помечено {result.Flagged}, " +
            $"снято {result.Cleared}, ручных сохранено {result.ManualKept}",
            new { eventId, competitionId, result.Scanned, result.Flagged, result.Cleared, result.ManualKept },
            HttpContext.RequestAborted);

        return Ok(result);
    }

    /// <summary>
    /// Поиск строки внутри скоупа, чтобы пометить её вручную. Автоматика ловит не всё:
    /// ошибка протокола может быть медленнее рекорда и не нарушать ни одного правила —
    /// её видит только человек, знающий пловца.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] int? eventId, [FromQuery] int? competitionId, [FromQuery] string? q)
    {
        if (eventId is null && competitionId is null)
            return BadRequest(new { error = "Нужен eventId или competitionId" });

        return Ok(await _service.SearchAsync(eventId, competitionId, q ?? "", ct: HttpContext.RequestAborted));
    }

    public sealed record ManualFlagRequest(bool Flagged, string? Note);

    /// <summary>Ручная пометка/снятие одной строки — переживает переимпорт и повторный скан.</summary>
    [HttpPost("{resultId:long}/manual")]
    public async Task<IActionResult> SetManual(long resultId, [FromBody] ManualFlagRequest request)
    {
        var ok = await _service.SetManualAsync(
            resultId, request.Flagged, request.Note, HttpContext.RequestAborted);
        if (!ok) return NotFound(new { error = $"Результат {resultId} не найден" });

        await _audit.LogAsync("result.quality-manual", "Result", resultId.ToString(),
            request.Flagged
                ? $"Помечен вручную как недостоверный: {request.Note}"
                : "Пометка снята вручную",
            new { resultId, request.Flagged, request.Note },
            HttpContext.RequestAborted);

        return Ok(new { resultId, request.Flagged });
    }
}
