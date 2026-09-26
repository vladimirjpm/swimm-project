using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Controllers;

/// <summary>
/// Уровни пловцов группы — карточка «Levels» в табе Admin страницы группы
/// (docs/plans/lane-plans-plan.md, L1). Только управляющим: владелец, админ группы,
/// site-админ (<see cref="HubGroupPermissions.CanEdit"/>). Данные приватные — без кэша.
/// Префикс тот же, что у остального самообслуживания групп (<c>MyHubGroupsController</c>).
/// </summary>
[ApiController]
[Route("api/me/hub-groups/{id:int}")]
[Authorize]
[AutoValidateAntiforgeryToken]
public class HubGroupLevelsController : ControllerBase
{
    private readonly IHubGroupLevelService _levels;
    private readonly IHubGroupPermissionService _permissions;

    public HubGroupLevelsController(IHubGroupLevelService levels, IHubGroupPermissionService permissions)
    {
        _levels = levels;
        _permissions = permissions;
    }

    /// <summary>null — права есть; иначе готовый отказ (401/404/403).</summary>
    private async Task<IActionResult?> RequireCanEditAsync(int hubGroupId)
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId)) return Unauthorized();

        var perms = await _permissions.GetPermissionsAsync(hubGroupId, userId, User.IsInRole("Admin"));
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();
        return null;
    }

    /// <summary>Уровни + состав с уровнями. Группа без уровней получает стандартный набор.</summary>
    [HttpGet("levels")]
    public async Task<IActionResult> GetLevels(int id)
    {
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        var dto = await _levels.GetAsync(id);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>Сохранить список уровней целиком (порядок = ранг); отвечает свежим состоянием.</summary>
    [HttpPut("levels")]
    public async Task<IActionResult> SaveLevels(int id, [FromBody] HubGroupLevelsInputDto input)
    {
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        var result = await _levels.SaveLevelsAsync(id, input);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(await _levels.GetAsync(id));
    }

    /// <summary>Поставить/снять уровень пловцу (<c>levelId: null</c> — снять).</summary>
    [HttpPut("swimmer-levels/{swimmerId:int}")]
    public async Task<IActionResult> SetSwimmerLevel(int id, int swimmerId, [FromBody] HubGroupSwimmerLevelInputDto input)
    {
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        var result = await _levels.SetSwimmerLevelAsync(id, swimmerId, input.LevelId);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }
}
