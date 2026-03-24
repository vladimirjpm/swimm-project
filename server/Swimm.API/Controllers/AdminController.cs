using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swimm.API.Data;
using Swimm.API.Services;

namespace Swimm.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly SwimmDbContext _db;
    private readonly DbSchemaService _schemaService;
    private readonly AdminSettingsService _settings;

    public AdminController(SwimmDbContext db, DbSchemaService schemaService, AdminSettingsService settings)
    {
        _db = db;
        _schemaService = schemaService;
        _settings = settings;
    }

    /// <summary>
    /// ?????? ????????????? ? ??????.
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _db.AppUsers
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.AvatarUrl,
                u.IsActive,
                u.CreatedAt,
                u.SwimmerId,
                u.ClubId,
                roles = u.UserRoles.Select(r => r.Role.Name).ToArray()
            })
            .ToListAsync();

        return Ok(users);
    }

    /// <summary>
    /// ?????? ????????? ?????.
    /// </summary>
    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _db.AppRoles
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.Name })
            .ToListAsync();

        return Ok(roles);
    }

    /// <summary>
    /// ????????? ???? ????????????.
    /// </summary>
    [HttpPost("users/{userId}/roles/{roleId}")]
    public async Task<IActionResult> AddRole(int userId, int roleId)
    {
        var userExists = await _db.AppUsers.AnyAsync(u => u.Id == userId);
        if (!userExists) return NotFound(new { error = "User not found" });

        var roleExists = await _db.AppRoles.AnyAsync(r => r.Id == roleId);
        if (!roleExists) return NotFound(new { error = "Role not found" });

        var already = await _db.AppUserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
        if (already) return Ok(new { message = "Role already assigned" });

        _db.AppUserRoles.Add(new Models.AppUserRole { UserId = userId, RoleId = roleId });
        await _db.SaveChangesAsync();

        return Ok(new { message = "Role added" });
    }

    /// <summary>
    /// ????? ???? ? ????????????.
    /// </summary>
    [HttpDelete("users/{userId}/roles/{roleId}")]
    public async Task<IActionResult> RemoveRole(int userId, int roleId)
    {
        var link = await _db.AppUserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId);

        if (link == null) return NotFound(new { error = "Role assignment not found" });

        _db.AppUserRoles.Remove(link);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Role removed" });
    }

    /// <summary>
    /// ????????/????????? ????????????.
    /// </summary>
    [HttpPatch("users/{userId}/active")]
    public async Task<IActionResult> SetActive(int userId, [FromBody] SetActiveRequest request)
    {
        var user = await _db.AppUsers.FindAsync(userId);
        if (user == null) return NotFound(new { error = "User not found" });

        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { message = user.IsActive ? "User activated" : "User deactivated" });
    }

    /// <summary>
    /// ?????????? ??? ????????.
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var usersCount = await _db.AppUsers.CountAsync();
        var resultsCount = await _db.Results.CountAsync();
        var competitionsCount = await _db.Competitions.CountAsync();
        var swimmersCount = await _db.Swimmers.CountAsync();
        var clubsCount = await _db.Clubs.CountAsync();

        return Ok(new
        {
            users = usersCount,
            results = resultsCount,
            competitions = competitionsCount,
            swimmers = swimmersCount,
            clubs = clubsCount
        });
    }

    /// <summary>
    /// ?????????? ????? ??: ???????, ???????, FK, ???????, CHECK, ?????????, ?????.
    /// </summary>
    [HttpGet("db-schema")]
    public async Task<IActionResult> GetDbSchema([FromQuery] bool refresh = false)
    {
        var schema = await _schemaService.GetSchemaAsync(refresh);
        return Ok(schema);
    }

    /// <summary>
    /// ??? ????????? ???????.
    /// </summary>
    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        return Ok(_settings.GetAll());
    }

    /// <summary>
    /// ???????? ???????? ?????????.
    /// </summary>
    [HttpPut("settings/{key}")]
    public IActionResult UpdateSetting(string key, [FromBody] UpdateSettingRequest request)
    {
        var updated = _settings.Update(key, request.Value);
        if (!updated)
            return BadRequest(new { error = "Invalid key or value type mismatch" });

        return Ok(_settings.Get(key));
    }

    public class SetActiveRequest
    {
        public bool IsActive { get; set; }
    }

    public class UpdateSettingRequest
    {
        public string Value { get; set; } = "";
    }
}
