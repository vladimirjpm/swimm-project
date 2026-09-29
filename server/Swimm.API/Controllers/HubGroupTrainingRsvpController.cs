using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Microsoft.AspNetCore.RateLimiting;
using Swimm.Application.Mapping;

namespace Swimm.API.Controllers;

/// <summary>
/// Ответы «иду / не уверен / не приду» на занятие группы (docs/plans/entity-hero-roles-plan.md,
/// Ш2). Смотрят управляющие и активные участники-аккаунты (та же аудитория, что у тренировок и
/// плана дорожек; pending не пускает); отвечает участник за себя, управляющий — и за участника.
/// Данные личные — без кэша и с <c>no-store</c>. Тестовая группа для не-тестового зрителя — 404
/// (права это знают).
///
/// Занятие — ключ <c>yyyy-MM-dd-HHmm</c> (`next_training.id` в ответе страницы группы).
/// </summary>
[ApiController]
[Route("api/hub-groups/{id:int}/rsvp")]
[Authorize]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting(HubGroupQuotaRules.RateLimitPolicy)]
public class HubGroupTrainingRsvpController : ControllerBase
{
    private readonly ITrainingRsvpService _rsvp;
    private readonly IHubGroupPermissionService _permissions;
    private readonly IHubGroupTrainingRepository _trainings;

    public HubGroupTrainingRsvpController(
        ITrainingRsvpService rsvp, IHubGroupPermissionService permissions, IHubGroupTrainingRepository trainings)
    {
        _rsvp = rsvp;
        _permissions = permissions;
        _trainings = trainings;
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Кто смотрит: управляющий, участник или никто (готовый отказ).</summary>
    private async Task<(int UserId, bool IsManager, IActionResult? Denied)> ViewerAsync(int hubGroupId)
    {
        var userId = CurrentUserId();
        if (userId == null) return (0, false, Unauthorized());

        var perms = await _permissions.GetPermissionsAsync(hubGroupId, userId.Value, User.IsInRole("Admin"));
        if (!perms.Exists) return (0, false, NotFound());
        if (perms.CanEdit) return (userId.Value, true, null);

        return await _trainings.IsActiveAccountMemberAsync(hubGroupId, userId.Value)
            ? (userId.Value, false, null)
            : (0, false, Forbid());
    }

    /// <summary>Ответы на занятие. 404 — такого занятия нет (не в расписании или вне окна).</summary>
    [HttpGet("{session}")]
    public async Task<IActionResult> Get(int id, string session)
    {
        var (userId, isManager, denied) = await ViewerAsync(id);
        if (denied != null) return denied;

        var dto = await _rsvp.GetAsync(id, session, userId, isManager);
        Response.Headers.CacheControl = "private, no-store";
        return dto == null ? NotFound(new { error = "Unknown training." }) : Ok(dto);
    }

    /// <summary>Поставить / снять ответ. Возвращает ответы занятия целиком — клиент сверяет оптимистичное.</summary>
    [HttpPut("{session}")]
    public async Task<IActionResult> Set(int id, string session, [FromBody] TrainingRsvpInputDto input)
    {
        var (userId, isManager, denied) = await ViewerAsync(id);
        if (denied != null) return denied;

        var result = await _rsvp.SetAsync(id, session, userId, isManager, input);
        Response.Headers.CacheControl = "private, no-store";
        if (result.Rsvp != null) return Ok(result.Rsvp);
        return StatusCode(result.Status, new { error = result.Error });
    }
}
