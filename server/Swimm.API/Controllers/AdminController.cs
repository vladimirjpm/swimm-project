using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class AdminController : ControllerBase
{
    private readonly IAdminRepository _admin;
    private readonly ISchemaService _schema;
    private readonly ISettingsService _settings;
    private readonly IImportService _import;
    private readonly IImportJobQueue _jobs;
    private readonly IResultRepository _results;

    public AdminController(
        IAdminRepository admin,
        ISchemaService schema,
        ISettingsService settings,
        IImportService import,
        IImportJobQueue jobs,
        IResultRepository results)
    {
        _admin = admin;
        _schema = schema;
        _settings = settings;
        _import = import;
        _jobs = jobs;
        _results = results;
    }

    // ── Users ────────────────────────────────────────────────────────────────

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
        => Ok(await _admin.GetUsersAsync());

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
        => Ok(await _admin.GetRolesAsync());

    [HttpPost("users/{userId}/roles/{roleId}")]
    public async Task<IActionResult> AddRole(int userId, int roleId)
    {
        var result = await _admin.AddRoleAsync(userId, roleId);
        return result switch
        {
            RoleOperationResult.Ok => Ok(new { message = "Role added" }),
            RoleOperationResult.UserNotFound => NotFound(new { error = "User not found" }),
            RoleOperationResult.RoleNotFound => NotFound(new { error = "Role not found" }),
            RoleOperationResult.AlreadyAssigned => Ok(new { message = "Role already assigned" }),
            _ => BadRequest()
        };
    }

    [HttpDelete("users/{userId}/roles/{roleId}")]
    public async Task<IActionResult> RemoveRole(int userId, int roleId)
    {
        var ok = await _admin.RemoveRoleAsync(userId, roleId);
        return ok ? Ok(new { message = "Role removed" }) : NotFound(new { error = "Role assignment not found" });
    }

    [HttpPatch("users/{userId}/active")]
    public async Task<IActionResult> SetActive(int userId, [FromBody] SetActiveRequest request)
    {
        var ok = await _admin.SetUserActiveAsync(userId, request.IsActive);
        if (!ok) return NotFound(new { error = "User not found" });
        return Ok(new { message = request.IsActive ? "User activated" : "User deactivated" });
    }

    [HttpGet("users/{userId}/details")]
    public async Task<IActionResult> GetUserDetails(int userId)
    {
        var detail = await _admin.GetUserDetailsAsync(userId);
        if (detail == null) return NotFound(new { error = "User not found" });
        return Ok(detail);
    }

    // ── Dashboard ────────────────────────────────────────────────────────────

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
        => Ok(await _admin.GetStatsAsync());

    // ── DB Schema ────────────────────────────────────────────────────────────

    [HttpGet("db-schema")]
    public async Task<IActionResult> GetDbSchema([FromQuery] bool refresh = false)
        => Ok(await _schema.GetSchemaAsync(refresh));

    // ── Settings ─────────────────────────────────────────────────────────────

    [HttpGet("settings")]
    public IActionResult GetSettings()
        => Ok(_settings.GetAll());

    [HttpPut("settings/{key}")]
    public IActionResult UpdateSetting(string key, [FromBody] UpdateSettingRequest request)
    {
        if (!_settings.Update(key, request.Value))
            return BadRequest(new { error = "Invalid key or value type mismatch" });
        return Ok(_settings.Get(key));
    }

    // ── Import ───────────────────────────────────────────────────────────────

    [HttpPost("import")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> ImportJson(
        IFormFile? file,
        [FromForm] string[]? categories,
        [FromForm] int? eventId,
        [FromForm] string? newEventName)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        if (!file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only .json files are accepted" });

        // Читаем файл в память один раз; это позволяет валидировать контент до постановки в очередь.
        using var ms = new MemoryStream((int)file.Length);
        await file.OpenReadStream().CopyToAsync(ms);
        var data = ms.ToArray();

        // Валидируем: файл должен начинаться с JSON-токена { или [ (игнорируя BOM и пробелы).
        if (!IsJsonContent(data))
            return BadRequest(new { error = "File content is not valid JSON (must start with '{' or '[')." });

        // Привязка к многодневному событию (опционально): existing eventId XOR newEventName.
        ImportEventOptions? eventOptions = null;
        if (eventId.HasValue || !string.IsNullOrWhiteSpace(newEventName))
            eventOptions = new ImportEventOptions(eventId, newEventName);

        var jobId = _jobs.Enqueue(data, file.FileName, categories, eventOptions);
        return Accepted(new { jobId });
    }

    [HttpGet("import/status/{jobId:guid}")]
    public IActionResult GetImportJobStatus(Guid jobId)
    {
        var status = _jobs.GetStatus(jobId);
        if (status == null)
            return NotFound(new { error = "Job not found" });

        return Ok(new
        {
            jobId = status.JobId,
            state = status.State.ToString().ToLowerInvariant(),
            queuedAt = status.QueuedAt,
            completedAt = status.CompletedAt,
            result = status.Result,
            error = status.Error
        });
    }

    private static bool IsJsonContent(byte[] data)
    {
        // Пропускаем UTF-8 BOM (EF BB BF) и leading whitespace, ищем { или [.
        var start = 0;
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            start = 3;
        for (var i = start; i < Math.Min(data.Length, 512); i++)
        {
            var b = data[i];
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') continue;
            return b is (byte)'{' or (byte)'[';
        }
        return false;
    }

    [HttpPost("swimmers/enrich")]
    public async Task<IActionResult> EnrichSwimmers()
    {
        try
        {
            var updated = await _import.EnrichSwimmersFromResultsAsync();
            return Ok(new { message = $"Обновлено спортсменов: {updated}", updated });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("clearable-tables")]
    public IActionResult GetClearableTables()
        => Ok(new { tables = _import.GetClearableTables() });

    [HttpDelete("import/clear")]
    public async Task<IActionResult> ClearImportData()
    {
        try
        {
            var result = await _import.ClearDataAsync();
            return Ok(new { message = $"Очистка завершена: удалено {result.Total} записей", deleted = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("import/competition/{id:int}")]
    public async Task<IActionResult> DeleteCompetition(int id)
    {
        try
        {
            var result = await _import.DeleteCompetitionAsync(id);
            if (result == null)
                return NotFound(new { error = $"Competition {id} not found" });

            return Ok(new
            {
                message = $"Соревнование «{result.CompetitionName}» удалено: {result.Results} результатов, {result.Relays} эстафет, {result.Galleries} галерей",
                deleted = result
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── Competitions ─────────────────────────────────────────────────────────

    [HttpGet("competitions")]
    public async Task<IActionResult> GetCompetitions()
        => Ok(await _admin.GetCompetitionsAsync());

    [HttpPatch("competitions/{id:int}")]
    public async Task<IActionResult> UpdateCompetitionFlags(int id, [FromBody] UpdateCompetitionFlagsRequest request)
    {
        var ok = await _admin.UpdateCompetitionFlagsAsync(id, request.IsMasters, request.IsAward, request.ShowCombineAllResults);
        if (!ok) return NotFound(new { error = $"Competition {id} not found" });
        return Ok(new { message = "Обновлено", id });
    }

    [HttpGet("competition-events")]
    public async Task<IActionResult> GetCompetitionEvents()
        => Ok(await _admin.GetCompetitionEventsAsync());

    // ── Import history ───────────────────────────────────────────────────────

    [HttpGet("import-history")]
    public async Task<IActionResult> GetImportHistory()
        => Ok(await _admin.GetImportHistoryAsync());

    [HttpPatch("import-history/{id}/approve")]
    public async Task<IActionResult> SetImportApproved(int id, [FromBody] SetApprovedRequest request)
    {
        var ok = await _admin.SetImportApprovedAsync(id, request.Approved);
        if (!ok) return NotFound(new { error = "Import history entry not found" });
        return Ok(new { message = request.Approved ? "Import approved" : "Import unapproved", id, request.Approved });
    }

    // ── Results filter hints ─────────────────────────────────────────────────

    private static readonly HashSet<string> _allowedHintFields =
        new(["style", "distance", "club", "competition", "name"]);

    [HttpGet("results/filter-hints")]
    public async Task<IActionResult> GetResultFilterHints(
        [FromQuery] string field,
        [FromQuery] string? q,
        [FromQuery] int limit = 20)
    {
        if (!_allowedHintFields.Contains(field))
            return BadRequest(new { error = "Invalid field" });

        return Ok(await _results.GetFilterHintsAsync(field, q, limit));
    }

    // ── Request types ────────────────────────────────────────────────────────

    public record SetApprovedRequest(bool Approved);
    public record SetActiveRequest(bool IsActive);
    public record UpdateSettingRequest(string Value);
    public record UpdateCompetitionFlagsRequest(bool IsMasters, bool IsAward, bool ShowCombineAllResults);
}
