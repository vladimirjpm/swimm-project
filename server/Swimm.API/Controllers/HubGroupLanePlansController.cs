using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;

namespace Swimm.API.Controllers;

/// <summary>
/// План дорожек группы на дату (docs/plans/lane-plans-plan.md, L2). Читают управляющие и
/// активные участники-аккаунты (та же аудитория, что у тренировок; pending не пускает),
/// участник — только опубликованные. Правят — управляющие (<see cref="HubGroupPermissions.CanEdit"/>).
/// Приватные данные — без кэша. Тестовая группа для не-тестового зрителя — 404 (права это знают).
/// </summary>
[ApiController]
[Route("api/hub-groups/{id:int}/lane-plans")]
[Authorize]
[AutoValidateAntiforgeryToken]
public class HubGroupLanePlansController : ControllerBase
{
    private readonly ILanePlanService _plans;
    private readonly IHubGroupPermissionService _permissions;
    private readonly IHubGroupTrainingRepository _trainings;

    public HubGroupLanePlansController(
        ILanePlanService plans, IHubGroupPermissionService permissions, IHubGroupTrainingRepository trainings)
    {
        _plans = plans;
        _permissions = permissions;
        _trainings = trainings;
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Кто смотрит: управляющий, участник или никто (готовый отказ).</summary>
    private async Task<(bool IsManager, IActionResult? Denied)> ViewerAsync(int hubGroupId)
    {
        var userId = CurrentUserId();
        if (userId == null) return (false, Unauthorized());

        var perms = await _permissions.GetPermissionsAsync(hubGroupId, userId.Value, User.IsInRole("Admin"));
        if (!perms.Exists) return (false, NotFound());
        if (perms.CanEdit) return (true, null);

        return await _trainings.IsActiveAccountMemberAsync(hubGroupId, userId.Value)
            ? (false, null)
            : (false, Forbid());
    }

    private async Task<IActionResult?> RequireCanEditAsync(int hubGroupId)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var perms = await _permissions.GetPermissionsAsync(hubGroupId, userId.Value, User.IsInRole("Admin"));
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();
        return null;
    }

    private BadRequestObjectResult BadDate() =>
        BadRequest(new { error = $"Date must look like {LanePlanRules.DateFormat}." });

    /// <summary>Планы группы, новые сверху (участнику — только опубликованные).</summary>
    [HttpGet]
    public async Task<IActionResult> List(int id)
    {
        var (isManager, denied) = await ViewerAsync(id);
        if (denied != null) return denied;
        return Ok(await _plans.ListAsync(id, isManager));
    }

    /// <summary>Доска на дату. 404 — плана нет (или участнику — он ещё черновик).</summary>
    [HttpGet("{date}")]
    public async Task<IActionResult> Get(int id, string date)
    {
        if (!LanePlanRules.TryParseDate(date, out var day)) return BadDate();
        var (isManager, denied) = await ViewerAsync(id);
        if (denied != null) return denied;

        var plan = await _plans.GetAsync(id, day, isManager, CurrentUserId());
        return plan == null ? NotFound() : Ok(plan);
    }

    /// <summary>Сохранить план целиком (новый — черновиком); отвечает свежей доской.</summary>
    [HttpPut("{date}")]
    public async Task<IActionResult> Save(int id, string date, [FromBody] LanePlanInputDto input)
    {
        if (!LanePlanRules.TryParseDate(date, out var day)) return BadDate();
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        var result = await _plans.SaveAsync(id, day, input, CurrentUserId()!.Value);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(await _plans.GetAsync(id, day, isManager: true, CurrentUserId()));
    }

    /// <summary>«Distribute»: раскладка по уровням. Ничего не сохраняет.</summary>
    [HttpPost("distribute")]
    public async Task<IActionResult> Distribute(int id, [FromBody] LanePlanDistributeInputDto input)
    {
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        var (result, error) = await _plans.DistributeAsync(id, input);
        return result == null ? BadRequest(new { error }) : Ok(result);
    }

    [HttpPost("{date}/publish")]
    public Task<IActionResult> Publish(int id, string date) => SetStatusAsync(id, date, LanePlanStatus.Published);

    [HttpPost("{date}/unpublish")]
    public Task<IActionResult> Unpublish(int id, string date) => SetStatusAsync(id, date, LanePlanStatus.Draft);

    [HttpDelete("{date}")]
    public async Task<IActionResult> Delete(int id, string date)
    {
        if (!LanePlanRules.TryParseDate(date, out var day)) return BadDate();
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        return await _plans.DeleteAsync(id, day) ? NoContent() : NotFound();
    }

    private async Task<IActionResult> SetStatusAsync(int id, string date, string status)
    {
        if (!LanePlanRules.TryParseDate(date, out var day)) return BadDate();
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        return await _plans.SetStatusAsync(id, day, status) ? NoContent() : NotFound();
    }
}
