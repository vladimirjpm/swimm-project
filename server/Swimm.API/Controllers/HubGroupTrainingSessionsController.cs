using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.API.Controllers;

/// <summary>
/// Тренировки группы как записи (docs/plans/group-events-media-plan.md, Е1): тренер заводит
/// тренировку кнопкой — и без времён («Underwater filming»), к ней потом видео. Смотрят
/// управляющие и активные участники-аккаунты (та же аудитория, что у журнала тренировок);
/// заводят, правят и удаляют — только управляющие; ссылки к тренировке добавляют и участники
/// (Е2, если группа не поставила «только тренер»). Данные личные — без кэша и с
/// <c>no-store</c>. Тестовая группа для не-тестового зрителя — 404 (права это знают).
/// </summary>
[ApiController]
[Route("api/hub-groups/{id:int}/sessions")]
[Authorize]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting(HubGroupQuotaRules.RateLimitPolicy)]
public class HubGroupTrainingSessionsController : ControllerBase
{
    private readonly ITrainingSessionService _sessions;
    private readonly IHubGroupPermissionService _permissions;
    private readonly IHubGroupTrainingRepository _trainings;

    public HubGroupTrainingSessionsController(
        ITrainingSessionService sessions, IHubGroupPermissionService permissions, IHubGroupTrainingRepository trainings)
    {
        _sessions = sessions;
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

    /// <summary>Управляющий — или готовый отказ (участнику 403).</summary>
    private async Task<(int UserId, IActionResult? Denied)> ManagerAsync(int hubGroupId)
    {
        var (userId, isManager, denied) = await ViewerAsync(hubGroupId);
        if (denied != null) return (0, denied);
        return isManager ? (userId, null) : (0, Forbid());
    }

    [HttpGet]
    public async Task<IActionResult> List(int id)
    {
        var (userId, isManager, denied) = await ViewerAsync(id);
        if (denied != null) return denied;

        Response.Headers.CacheControl = "private, no-store";
        return Ok(await _sessions.ListAsync(id, userId, isManager));
    }

    /// <summary>Зритель открыл ленту (Т1б): гаснет метка «новые видео».</summary>
    [HttpPut("seen")]
    public async Task<IActionResult> Seen(int id)
    {
        var (userId, _, denied) = await ViewerAsync(id);
        if (denied != null) return denied;

        await _sessions.MarkSeenAsync(id, userId);
        Response.Headers.CacheControl = "private, no-store";
        return NoContent();
    }

    [HttpPost]
    public async Task<IActionResult> Create(int id, [FromBody] TrainingSessionInputDto input)
    {
        var (userId, denied) = await ManagerAsync(id);
        if (denied != null) return denied;

        return Reply(await _sessions.CreateAsync(id, userId, input));
    }

    [HttpPut("{sessionId:int}")]
    public async Task<IActionResult> Update(int id, int sessionId, [FromBody] TrainingSessionInputDto input)
    {
        var (userId, denied) = await ManagerAsync(id);
        if (denied != null) return denied;

        return Reply(await _sessions.UpdateAsync(id, sessionId, userId, input));
    }

    /// <summary>«Sets &amp; times» (Т1а): лениво, при раскрытии карточки; та же аудитория, что у списка.</summary>
    [HttpGet("{sessionId:int}/sets")]
    public async Task<IActionResult> Sets(int id, int sessionId)
    {
        var (userId, isManager, denied) = await ViewerAsync(id);
        if (denied != null) return denied;

        var sets = await _sessions.GetSetsAsync(id, sessionId, userId, isManager);
        Response.Headers.CacheControl = "private, no-store";
        return sets == null ? NotFound(new { error = "Training not found." }) : Ok(sets);
    }

    /// <summary>★ ключевого сета (Р4) — только управляющий; <c>set_no: null</c> снимает.</summary>
    [HttpPut("{sessionId:int}/key-set")]
    public async Task<IActionResult> KeySet(int id, int sessionId, [FromBody] TrainingKeySetInputDto input)
    {
        var (userId, denied) = await ManagerAsync(id);
        if (denied != null) return denied;

        return Reply(await _sessions.SetKeySetAsync(id, sessionId, userId, input));
    }

    /// <summary>Только тренировку без времён; её медиа уходит с ней.</summary>
    [HttpDelete("{sessionId:int}")]
    public async Task<IActionResult> Delete(int id, int sessionId)
    {
        var (_, denied) = await ManagerAsync(id);
        if (denied != null) return denied;

        var (status, error) = await _sessions.DeleteAsync(id, sessionId);
        Response.Headers.CacheControl = "private, no-store";
        return error == null ? NoContent() : StatusCode(status, new { error });
    }

    /// <summary>
    /// Ссылка к тренировке (Е2): управляющий — всегда, активный участник — если группа не
    /// поставила «только тренер». Видна той же аудитории, что и тренировка.
    /// </summary>
    [HttpPost("{sessionId:int}/media")]
    public async Task<IActionResult> AddMedia(int id, int sessionId, [FromBody] TrainingSessionMediaInputDto input)
    {
        var (userId, isManager, denied) = await ViewerAsync(id);
        if (denied != null) return denied;

        var result = await _sessions.AddMediaAsync(id, sessionId, userId, isManager, input);
        Response.Headers.CacheControl = "private, no-store";
        if (result.Media != null) return Ok(result.Media);
        return StatusCode(result.Status, new { error = result.Error });
    }

    /// <summary>
    /// Сменить / снять метку «кто на видео» у ссылки — только управляющий;
    /// <c>swimmer_id: null</c> снимает. Метка прав и видимости не меняет.
    /// </summary>
    [HttpPut("{sessionId:int}/media/{mediaId:int}/swimmer")]
    public async Task<IActionResult> SetMediaSwimmer(int id, int sessionId, int mediaId,
        [FromBody] TrainingMediaSwimmerInputDto input)
    {
        var (userId, denied) = await ManagerAsync(id);
        if (denied != null) return denied;

        var result = await _sessions.SetMediaSwimmerAsync(id, sessionId, mediaId, userId, input);
        Response.Headers.CacheControl = "private, no-store";
        if (result.Media != null) return Ok(result.Media);
        return StatusCode(result.Status, new { error = result.Error });
    }

    /// <summary>Удалить ссылку: автор — свою, управляющий — любую.</summary>
    [HttpDelete("{sessionId:int}/media/{mediaId:int}")]
    public async Task<IActionResult> DeleteMedia(int id, int sessionId, int mediaId)
    {
        var (userId, isManager, denied) = await ViewerAsync(id);
        if (denied != null) return denied;

        var (status, error) = await _sessions.DeleteMediaAsync(id, sessionId, mediaId, userId, isManager);
        Response.Headers.CacheControl = "private, no-store";
        return error == null ? NoContent() : StatusCode(status, new { error });
    }

    private IActionResult Reply(TrainingSessionSaveResult result)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (result.Session != null) return Ok(result.Session);
        return StatusCode(result.Status, new { error = result.Error });
    }
}
