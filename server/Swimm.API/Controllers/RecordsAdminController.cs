using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;

namespace Swimm.API.Controllers;

/// <summary>
/// Админский CRUD рекордов и нормативов (Admin/Records). Отдельный контроллер от
/// <see cref="AdminController"/> — предметная область самодостаточна (см.
/// <see cref="IRecordAdminRepository"/>).
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class RecordsAdminController : ControllerBase
{
    private const int DefaultPageSize = 50;

    private readonly IRecordAdminRepository _repo;
    private readonly IRecordQualityService _quality;

    public RecordsAdminController(IRecordAdminRepository repo, IRecordQualityService quality)
    {
        _repo = repo;
        _quality = quality;
    }

    // ── Спорные записи справочника (docs/plans/records-quality-plan.md) ──────
    // ⚠ Ошибки источника мы НЕ чиним правкой Records: наша копия обязана совпадать с
    // файлом федерации. Здесь только реестр претензий.

    [HttpGet("record-issues")]
    public async Task<IActionResult> GetRecordIssues(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
        => Ok(await _quality.ListIssuesAsync(status, Math.Max(page, 1), pageSize));

    [HttpPost("record-issues")]
    public async Task<IActionResult> CreateRecordIssue([FromBody] RecordIssueInputDto input)
    {
        if (string.IsNullOrWhiteSpace(input.FlaggedTime))
            return BadRequest(new { error = "Нужно указать оспариваемое время" });
        if (input.Reason != null && !RecordIssueReasons.All.Contains(input.Reason))
            return BadRequest(new { error = $"Причина должна быть одной из: {string.Join(", ", RecordIssueReasons.All)}" });

        var createdBy = User.Identity?.Name ?? "admin";
        return Ok(await _quality.CreateIssueAsync(input, createdBy));
    }

    [HttpPatch("record-issues/{id:int}")]
    public async Task<IActionResult> UpdateRecordIssue(int id, [FromBody] RecordIssueUpdateDto update)
    {
        if (update.Status != null && !RecordIssueStatuses.All.Contains(update.Status))
            return BadRequest(new { error = $"Статус должен быть одним из: {string.Join(", ", RecordIssueStatuses.All)}" });
        if (update.Reason != null && !RecordIssueReasons.All.Contains(update.Reason))
            return BadRequest(new { error = $"Причина должна быть одной из: {string.Join(", ", RecordIssueReasons.All)}" });

        var dto = await _quality.UpdateIssueAsync(id, update);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpDelete("record-issues/{id:int}")]
    public async Task<IActionResult> DeleteRecordIssue(int id)
        => await _quality.DeleteIssueAsync(id) ? NoContent() : NotFound();

    // ── Records ──────────────────────────────────────────────────────────────

    [HttpGet("records")]
    public async Task<IActionResult> GetRecords(
        [FromQuery] string? regionType,
        [FromQuery] string? regionCode,
        [FromQuery] string? category,
        [FromQuery] string? gender,
        [FromQuery] string? poolType,
        [FromQuery] string? style,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        var filter = new RecordFilter(regionType, regionCode, category, gender, poolType, style);
        return Ok(await _repo.GetRecordsAsync(filter, Math.Max(page, 1), pageSize));
    }

    [HttpPost("records")]
    public async Task<IActionResult> CreateRecord([FromBody] RecordInputDto input)
    {
        var result = await _repo.CreateRecordAsync(input);
        return result.Success ? Ok(new { message = "Рекорд создан", id = result.Id }) : BadRequest(new { error = result.Error });
    }

    [HttpPatch("records/{id:int}")]
    public async Task<IActionResult> UpdateRecord(int id, [FromBody] RecordQuickEditDto input)
    {
        var result = await _repo.UpdateRecordAsync(id, input);
        return result.Success ? Ok(new { message = "Сохранено", id = result.Id }) : BadRequest(new { error = result.Error });
    }

    [HttpDelete("records/{id:int}")]
    public async Task<IActionResult> DeleteRecord(int id)
    {
        var result = await _repo.DeleteRecordAsync(id);
        return result.Success ? Ok(new { message = "Рекорд удалён", id = result.Id }) : NotFound(new { error = result.Error });
    }

    // ── NormativeStandards ───────────────────────────────────────────────────

    [HttpGet("normative-standards")]
    public async Task<IActionResult> GetStandards(
        [FromQuery] string? kind,
        [FromQuery] string? gender,
        [FromQuery] string? poolType,
        [FromQuery] string? style,
        [FromQuery] string? country,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        var filter = new StandardFilter(kind, gender, poolType, style, country);
        return Ok(await _repo.GetStandardsAsync(filter, Math.Max(page, 1), pageSize));
    }

    [HttpPost("normative-standards")]
    public async Task<IActionResult> CreateStandard([FromBody] NormativeStandardInputDto input)
    {
        var result = await _repo.CreateStandardAsync(input);
        return result.Success ? Ok(new { message = "Норматив создан", id = result.Id }) : BadRequest(new { error = result.Error });
    }

    [HttpPatch("normative-standards/{id:int}")]
    public async Task<IActionResult> UpdateStandard(int id, [FromBody] StandardQuickEditDto input)
    {
        var result = await _repo.UpdateStandardAsync(id, input);
        return result.Success ? Ok(new { message = "Сохранено", id = result.Id }) : BadRequest(new { error = result.Error });
    }

    [HttpDelete("normative-standards/{id:int}")]
    public async Task<IActionResult> DeleteStandard(int id)
    {
        var result = await _repo.DeleteStandardAsync(id);
        return result.Success ? Ok(new { message = "Норматив удалён", id = result.Id }) : NotFound(new { error = result.Error });
    }
}
