using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.API.Controllers;

/// <summary>
/// Флаг «On break» людей группы (docs/plans/entity-hero-roles-plan.md §5, Ш3.1). Смотрят и
/// ставят управляющие и активные участники-аккаунты (та же аудитория, что у тренировок и RSVP);
/// участник — только себе и только до даты, управляющий — любому, в том числе пловцу состава
/// без аккаунта и бессрочно. Данные личные — без кэша и с <c>no-store</c>.
/// </summary>
[ApiController]
[Route("api/hub-groups/{id:int}/breaks")]
[Authorize]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting(HubGroupQuotaRules.RateLimitPolicy)]
public class HubGroupBreaksController : ControllerBase
{
    private readonly IHubGroupBreakService _breaks;
    private readonly IHubGroupPermissionService _permissions;
    private readonly IHubGroupTrainingRepository _trainings;

    public HubGroupBreaksController(
        IHubGroupBreakService breaks, IHubGroupPermissionService permissions, IHubGroupTrainingRepository trainings)
    {
        _breaks = breaks;
        _permissions = permissions;
        _trainings = trainings;
    }

    /// <summary>Кто смотрит: управляющий, участник или никто (готовый отказ) — как у RSVP.</summary>
    private async Task<(int UserId, bool IsManager, IActionResult? Denied)> ViewerAsync(int hubGroupId)
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId)) return (0, false, Unauthorized());

        var perms = await _permissions.GetPermissionsAsync(hubGroupId, userId, User.IsInRole("Admin"));
        if (!perms.Exists) return (0, false, NotFound());
        if (perms.CanEdit) return (userId, true, null);

        return await _trainings.IsActiveAccountMemberAsync(hubGroupId, userId)
            ? (userId, false, null)
            : (0, false, Forbid());
    }

    /// <summary>Свой перерыв; управляющему — ещё все действующие и недавние возвращения.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(int id)
    {
        var (userId, isManager, denied) = await ViewerAsync(id);
        if (denied != null) return denied;

        var dto = await _breaks.GetAsync(id, userId, isManager);
        Response.Headers.CacheControl = "private, no-store";
        return Ok(dto);
    }

    /// <summary>Поставить / снять перерыв. Отвечает свежим состоянием.</summary>
    [HttpPut]
    public async Task<IActionResult> Set(int id, [FromBody] HubGroupBreakInputDto input)
    {
        var (userId, isManager, denied) = await ViewerAsync(id);
        if (denied != null) return denied;

        var result = await _breaks.SetAsync(id, userId, isManager, input);
        Response.Headers.CacheControl = "private, no-store";
        if (result.Breaks != null) return Ok(result.Breaks);
        return StatusCode(result.Status, new { error = result.Error });
    }
}
