using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swimm.Infrastructure.Data;
using Swimm.API.Services;
using Swimm.Domain.Entities;

namespace Swimm.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly SwimmDbContext _db;
    private readonly DbSchemaService _schemaService;
    private readonly AdminSettingsService _settings;
    private readonly JsonImportService _import;

    public AdminController(SwimmDbContext db, DbSchemaService schemaService, AdminSettingsService settings, JsonImportService import)
    {
        _db = db;
        _schemaService = schemaService;
        _settings = settings;
        _import = import;
    }

    /// <summary>
    /// Список пользователей и ролей.
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
                roles = u.UserRoles.Select(r => r.Role.Name).ToArray()
            })
            .ToListAsync();

        return Ok(users);
    }

    /// <summary>
    /// Список доступных ролей.
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
    /// Назначить роль пользователю.
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

        _db.AppUserRoles.Add(new AppUserRole { UserId = userId, RoleId = roleId });
        await _db.SaveChangesAsync();

        return Ok(new { message = "Role added" });
    }

    /// <summary>
    /// Снять роль с пользователя.
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
    /// Активировать/деактивировать пользователя.
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
    /// Статистика для дашборда.
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
    /// Структура схемы БД: таблицы, колонки, FK, индексы, CHECK, процедуры, вьюхи.
    /// </summary>
    [HttpGet("db-schema")]
    public async Task<IActionResult> GetDbSchema([FromQuery] bool refresh = false)
    {
        var schema = await _schemaService.GetSchemaAsync(refresh);
        return Ok(schema);
    }

    /// <summary>
    /// Все настройки сервера.
    /// </summary>
    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        return Ok(_settings.GetAll());
    }

    /// <summary>
    /// Обновить значение настройки.
    /// </summary>
    [HttpPut("settings/{key}")]
    public IActionResult UpdateSetting(string key, [FromBody] UpdateSettingRequest request)
    {
        var updated = _settings.Update(key, request.Value);
        if (!updated)
            return BadRequest(new { error = "Invalid key or value type mismatch" });

        return Ok(_settings.Get(key));
    }

    /// <summary>
    /// Импорт результатов из JSON-файла.
    /// Принимает multipart/form-data с полем file (JSON).
    /// Заполняет справочники и таблицу Results.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB
    public async Task<IActionResult> ImportJson(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        if (!file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only .json files are accepted" });

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _import.ImportAsync(stream, file.FileName);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Дополнение данных спортсменов (Gender, ClubId, CountryId) из таблицы Results.
    /// Заполняет пустые поля на основе самого свежего результата каждого спортсмена.
    /// Используется для обратного заполнения ранее импортированных данных.
    /// </summary>
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

    /// <summary>
    /// Список таблиц, которые будут очищены при вызове import/clear.
    /// Источник: <see cref="JsonImportService.ClearableTables"/>.
    /// </summary>
    [HttpGet("clearable-tables")]
    public IActionResult GetClearableTables()
    {
        return Ok(new { tables = JsonImportService.ClearableTables });
    }

    /// <summary>
    /// Полная очистка импортированных данных.
    /// Удаляет Results, Competitions, Clubs, Swimmers, Countries, Relays, Galleries, GalleryItems.
    /// Справочник Styles и пользовательские таблицы сохраняются.
    /// </summary>
    [HttpDelete("import/clear")]
    public async Task<IActionResult> ClearImportData()
    {
        try
        {
            var result = await _import.ClearDataAsync();
            return Ok(new
            {
                message = $"Очистка завершена: удалено {result.Total} записей",
                deleted = result
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// История импортов с названиями соревнований.
    /// </summary>
    [HttpGet("import-history")]
    public async Task<IActionResult> GetImportHistory()
    {
        var history = await _db.ImportHistory
            .AsNoTracking()
            .Include(h => h.Competition)
            .OrderByDescending(h => h.ImportDate)
            .Select(h => new
            {
                h.Id,
                h.CompetitionId,
                CompetitionName = h.Competition!.Name,
                CompetitionDate = h.Competition.Date,
                h.ImportFileName,
                h.ImportDate,
                h.Approved
            })
            .ToListAsync();

        return Ok(history);
    }

    /// <summary>
    /// Обновить статус подтверждения импорта.
    /// </summary>
    [HttpPatch("import-history/{id}/approve")]
    public async Task<IActionResult> SetImportApproved(int id, [FromBody] SetApprovedRequest request)
    {
        var entry = await _db.ImportHistory.FindAsync(id);
        if (entry == null) return NotFound(new { error = "Import history entry not found" });

        entry.Approved = request.Approved;
        await _db.SaveChangesAsync();

        return Ok(new { message = entry.Approved ? "Import approved" : "Import unapproved", entry.Id, entry.Approved });
    }

    public class SetApprovedRequest
    {
        public bool Approved { get; set; }
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
