using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminRepository _admin;
    private readonly ISchemaService _schema;
    private readonly ISettingsService _settings;
    private readonly IImportService _import;

    public AdminController(
        IAdminRepository admin,
        ISchemaService schema,
        ISettingsService settings,
        IImportService import)
    {
        _admin = admin;
        _schema = schema;
        _settings = settings;
        _import = import;
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
    public async Task<IActionResult> ImportJson(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        if (!file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only .json files are accepted" });

        try
        {
            await using var stream = file.OpenReadStream();
            return Ok(await _import.ImportAsync(stream, file.FileName));
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
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

    // ── Request types ────────────────────────────────────────────────────────

    public record SetApprovedRequest(bool Approved);
    public record SetActiveRequest(bool IsActive);
    public record UpdateSettingRequest(string Value);
}
