using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.API.Controllers;

/// <summary>
/// «Set up profile» — пункт меню с прогрессом и страница /setup-profile
/// (docs/plans/profile-setup-plan.md). Только своё; ответ личный, поэтому без кэша.
/// </summary>
[ApiController]
[Route("api/me/profile-setup")]
[Authorize]
[AutoValidateAntiforgeryToken]
public class ProfileSetupController : ControllerBase
{
    private readonly IProfileSetupService _setup;

    public ProfileSetupController(IProfileSetupService setup)
    {
        _setup = setup;
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Шаги, счётчик, видимость пункта и ответ опроса.</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var dto = await _setup.GetAsync(userId.Value);
        return dto == null ? Unauthorized() : Ok(dto);
    }

    /// <summary>
    /// Ответ опроса «Who are you?» — все четыре разом. Прав не даёт (Р1 плана); на шаги влияет
    /// только «I'm a parent» — добавляет шаг «My family» (хендофф ex3).
    /// </summary>
    [HttpPut("roles")]
    public async Task<IActionResult> SaveRoles([FromBody] ProfileSurveyRequest request)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        if (!ProfileSetupRules.IsValidSurvey(request.Swimmer, request.Parent, request.Coach, request.Watcher))
            return BadRequest(new
            {
                error = "'Just watching' can't be combined with other roles.",
                code = ProfileSetupRules.WatcherExclusiveErrorCode,
            });

        return await _setup.SaveSurveyAsync(userId.Value, request) ? NoContent() : Unauthorized();
    }

    /// <summary>
    /// Отказ от шага («Not a swimmer», «No club», «No group») — засчитывает шаг (решение Влада
    /// 17.10.2026). <c>key</c> — me | family | club | group.
    /// </summary>
    [HttpPut("steps/{key}/skip")]
    public Task<IActionResult> SkipStep(string key) => SetSkipped(key, true);

    /// <summary>Отменить отказ от шага.</summary>
    [HttpDelete("steps/{key}/skip")]
    public Task<IActionResult> UnskipStep(string key) => SetSkipped(key, false);

    private async Task<IActionResult> SetSkipped(string key, bool skipped)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();
        if (!ProfileSetupRules.IsStepKey(key)) return NotFound(new { error = "Unknown step." });

        return await _setup.SetStepSkippedAsync(userId.Value, key, skipped) ? NoContent() : Unauthorized();
    }

    /// <summary>Dismiss: пункт «Set up profile» больше не показывается в меню.</summary>
    [HttpPost("dismiss")]
    public async Task<IActionResult> Dismiss()
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        return await _setup.SetDismissedAsync(userId.Value, true) ? NoContent() : Unauthorized();
    }

    /// <summary>Вернуть пункт после Dismiss (UI пока нет — открытое решение плана).</summary>
    [HttpDelete("dismiss")]
    public async Task<IActionResult> Undismiss()
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        return await _setup.SetDismissedAsync(userId.Value, false) ? NoContent() : Unauthorized();
    }
}
