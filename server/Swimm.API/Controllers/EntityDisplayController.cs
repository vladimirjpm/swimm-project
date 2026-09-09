using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Controllers;

/// <summary>
/// Настройки ОТОБРАЖЕНИЯ страницы коллектива — фото шапки и его показ.
///
/// Один эндпоинт на клуб и на группу: это два вида одного — коллектив пловцов
/// (docs/plans/entity-page-shell-plan.md §3.10). Разное у них только право решать:
///
///  • клуб  — админ сайта. Владельцев у клуба не существует; появятся («claim your club») —
///            гейт станет таким же, как у группы, и больше ничего менять не придётся;
///  • группа — владелец / админ группы / админ сайта (та же проверка, что у инбокса
///            публикаций и тренировок: `IHubGroupPermissionService`).
///
/// Правка идёт из таба `Admin` страницы (§3.8): управление сущностью не рассыпается
/// кнопками по карточкам.
/// </summary>
[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
[Route("api/display-settings")]
public class EntityDisplayController : ControllerBase
{
    private readonly IEntityDisplayRepository _display;
    private readonly IHubGroupPermissionService _permissions;
    private readonly IAdminAuditService _audit;
    private readonly ICacheService _cache;

    public EntityDisplayController(
        IEntityDisplayRepository display, IHubGroupPermissionService permissions,
        IAdminAuditService audit, ICacheService cache)
    {
        _display = display;
        _permissions = permissions;
        _audit = audit;
        _cache = cache;
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Настройки клуба. Только админ сайта.</summary>
    [HttpPut("club/{id:int}")]
    public async Task<IActionResult> UpdateClub(int id, [FromBody] EntityDisplayInputDto input)
    {
        if (!User.IsInRole("Admin")) return Forbid();

        if (!await _display.UpdateClubAsync(id, input)) return NotFound(new { error = "club not found" });

        await _audit.LogAsync("club.display-settings", "Club", id.ToString(),
            $"hero: show={input.ShowHeroImage}, mediaId={input.HeroMediaId?.ToString() ?? "—"}", input);
        // Страница клуба кэшируется целиком — без сброса правка не видна до истечения TTL.
        await _cache.InvalidateAllAsync();
        return NoContent();
    }

    /// <summary>Настройки группы. Владелец / админ группы / админ сайта.</summary>
    [HttpPut("group/{id:int}")]
    public async Task<IActionResult> UpdateGroup(int id, [FromBody] EntityDisplayInputDto input)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var perms = await _permissions.GetPermissionsAsync(id, userId.Value, User.IsInRole("Admin"));
        if (!perms.Exists) return NotFound(new { error = "group not found" });
        if (!perms.CanEdit) return Forbid();

        if (!await _display.UpdateGroupAsync(id, input)) return NotFound(new { error = "group not found" });

        await _audit.LogAsync("hub-group.display-settings", "HubGroup", id.ToString(),
            $"hero: show={input.ShowHeroImage}, mediaId={input.HeroMediaId?.ToString() ?? "—"}", input);
        await _cache.InvalidateAllAsync();
        return NoContent();
    }
}
