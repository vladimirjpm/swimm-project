using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swimm.API.Data;
using Swimm.API.Services;

namespace Swimm.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly SwimmDbContext _db;
    private readonly DbSchemaService _schema;
    private readonly AdminSettingsService _settings;

    public AdminController(SwimmDbContext db, DbSchemaService schema, AdminSettingsService settings)
    {
        _db = db;
        _schema = schema;
        _settings = settings;
    }

    // ──── Пользователи ────

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new
            {
                u.Id, u.DisplayName, u.Email, u.AvatarUrl, u.IsActive,
                u.CreatedAt, u.LastLoginAt,
                roles = u.UserRoles.Select(ur => ur.Role.Name)
            })
            .ToListAsync();
        return Ok(users);
    }

    // ──── Роли ────

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _db.Roles.OrderBy(r => r.Name)
            .Select(r => new { r.Id, r.Name, userCount = r.UserRoles.Count })
            .ToListAsync();
        return Ok(roles);
    }

    [HttpPost("users/{userId}/roles/{roleName}")]
    public async Task<IActionResult> AddRole(int userId, string roleName)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
        if (role == null) return NotFound($"Role {roleName} not found.");
        if (await _db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == role.Id))
            return Conflict("User already has this role.");
        _db.UserRoles.Add(new Models.AppUserRole { UserId = userId, RoleId = role.Id });
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("users/{userId}/roles/{roleName}")]
    public async Task<IActionResult> RemoveRole(int userId, string roleName)
    {
        var link = await _db.UserRoles
            .Include(ur => ur.Role)
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.Role.Name == roleName);
        if (link == null) return NotFound();
        _db.UserRoles.Remove(link);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ──── Активация ────

    public record SetActiveRequest(bool IsActive);

    [HttpPut("users/{userId}/active")]
    public async Task<IActionResult> SetActive(int userId, [FromBody] SetActiveRequest req)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();
        user.IsActive = req.IsActive;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ──── Статистика ────

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        return Ok(new
        {
            totalUsers = await _db.Users.CountAsync(),
            activeUsers = await _db.Users.CountAsync(u => u.IsActive),
            totalResults = await _db.Results.CountAsync(),
            totalCompetitions = await _db.Competitions.CountAsync(),
            totalSwimmers = await _db.Swimmers.CountAsync()
        });
    }

    // ──── Схема БД ────

    [HttpGet("db-schema")]
    public async Task<IActionResult> GetDbSchema([FromQuery] bool refresh = false)
    {
        var data = await _schema.GetSchemaAsync(refresh);
        return Ok(data);
    }

    // ──── Настройки ────

    [HttpGet("settings")]
    public IActionResult GetSettings() => Ok(_settings.GetAll());

    public record UpdateSettingRequest(string Value);

    [HttpPut("settings/{key}")]
    public IActionResult UpdateSetting(string key, [FromBody] UpdateSettingRequest req)
    {
        var ok = _settings.Update(key, req.Value);
        return ok ? Ok(_settings.Get(key)) : BadRequest(new { error = $"Failed to update setting '{key}'" });
    }
}
