using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Microsoft.AspNetCore.RateLimiting;

namespace Swimm.API.Controllers;

/// <summary>
/// План дорожек занятия группы (docs/plans/lane-plans-plan.md, L2). Адрес плана — ключ занятия
/// <c>yyyy-MM-dd-HHmm</c>, тот же, что у <c>/rsvp/{id}</c> (CF5 плана trainings-coach-flow); голая дата
/// <c>yyyy-MM-dd</c> — старые ссылки и план дня без занятия, только для чтения. Читают управляющие и
/// активные участники-аккаунты (та же аудитория, что у тренировок; pending не пускает),
/// участник — только опубликованные. Правят — управляющие (<see cref="HubGroupPermissions.CanEdit"/>).
/// Приватные данные — без кэша. Тестовая группа для не-тестового зрителя — 404 (права это знают).
/// </summary>
[ApiController]
[Route("api/hub-groups/{id:int}/lane-plans")]
[Authorize]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting(HubGroupQuotaRules.RateLimitPolicy)]
public class HubGroupLanePlansController : ControllerBase
{
    private readonly ILanePlanService _plans;
    private readonly IHubGroupPermissionService _permissions;
    private readonly IHubGroupTrainingRepository _trainings;

    private readonly ILanePlanNotifier _notifier;

    public HubGroupLanePlansController(
        ILanePlanService plans, IHubGroupPermissionService permissions, IHubGroupTrainingRepository trainings,
        ILanePlanNotifier notifier)
    {
        _notifier = notifier;
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

    private BadRequestObjectResult BadKey() =>
        BadRequest(new { error = $"Plan key must look like yyyy-MM-dd-HHmm (or {LanePlanRules.DateFormat} to read a day plan)." });

    /// <summary>Ключ плана для ЗАПИСИ: только занятие; голая дата — план дня, архив только для чтения (CF5).</summary>
    private bool TryWriteKey(string raw, out LanePlanKey key, out IActionResult? bad)
    {
        bad = null;
        if (!LanePlanKey.TryParse(raw, out key)) { bad = BadKey(); return false; }
        if (!key.IsSession) { bad = BadRequest(new { error = LanePlanRules.ReadOnlyDayPlanMessage }); return false; }
        return true;
    }

    /// <summary>Планы группы, новые сверху (участнику — только опубликованные).</summary>
    [HttpGet]
    public async Task<IActionResult> List(int id)
    {
        var (isManager, denied) = await ViewerAsync(id);
        if (denied != null) return denied;
        return Ok(await _plans.ListAsync(id, isManager));
    }

    /// <summary>
    /// Зритель открыл вид Lanes (Т1б, `marks-spec.md`): гаснет золотая метка «тебя перенесли».
    /// Отдельной ручкой, а не побочным эффектом GET плана: план грузится и ради метки на табе
    /// группы — такой GET гасил бы метку раньше, чем человек её увидел.
    /// </summary>
    [HttpPut("seen")]
    public async Task<IActionResult> Seen(int id)
    {
        var (_, denied) = await ViewerAsync(id);
        if (denied != null) return denied;

        await _plans.MarkSeenAsync(id, CurrentUserId()!.Value);
        Response.Headers.CacheControl = "private, no-store";
        return NoContent();
    }

    /// <summary>
    /// Доска занятия. 404 — плана нет (или участнику — он ещё черновик). По голой дате — план дня без
    /// занятия или первый план этой даты (старые ссылки участников на Lanes).
    /// </summary>
    [HttpGet("{key}")]
    public async Task<IActionResult> Get(int id, string key)
    {
        if (!LanePlanKey.TryParse(key, out var day)) return BadKey();
        var (isManager, denied) = await ViewerAsync(id);
        if (denied != null) return denied;

        var plan = await _plans.GetAsync(id, day, isManager, CurrentUserId());
        return plan == null ? NotFound() : Ok(plan);
    }

    /// <summary>
    /// Кто в день плана «не в воде» (On break / Doesn't train / «Can't come») — редактор кладёт их в «Not today»
    /// нового плана. Только управляющему: статусы людей участникам не раздаём.
    /// </summary>
    [HttpGet("{key}/away")]
    public async Task<IActionResult> Away(int id, string key)
    {
        if (!LanePlanKey.TryParse(key, out var day)) return BadKey();
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        return Ok(await _plans.GetAwayAsync(id, day));
    }

    /// <summary>Сохранить план целиком (новый — черновиком); отвечает свежей доской.</summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> Save(int id, string key, [FromBody] LanePlanInputDto input)
    {
        if (!TryWriteKey(key, out var day, out var bad)) return bad!;
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        var result = await _plans.SaveAsync(id, day, input, CurrentUserId()!.Value);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(await _plans.GetAsync(id, day, isManager: true, CurrentUserId()));
    }

    /// <summary>
    /// Workout занятия (CF6, §5.5 плана trainings-coach-flow): документ целиком, <c>workout</c> null —
    /// стереть. Отдельно от PUT плана: редактор дорожек и редактор workout сохраняются сами и не
    /// перетирают друг друга. 404 — у занятия плана нет (workout живёт при плане).
    /// </summary>
    [HttpPut("{key}/workout")]
    public async Task<IActionResult> SaveWorkout(int id, string key, [FromBody] WorkoutInputDto input)
    {
        if (!TryWriteKey(key, out var day, out var bad)) return bad!;
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        var (found, error) = await _plans.SaveWorkoutAsync(id, day, input?.Workout);
        if (!found) return NotFound();
        if (error != null) return BadRequest(new { error });
        return NoContent();
    }

    /// <summary>«Auto lanes»: дорожки между уровнями по числу пришедших + раскладка. Ничего не сохраняет.</summary>
    [HttpPost("auto-lanes")]
    public async Task<IActionResult> AutoLanes(int id, [FromBody] LanePlanAutoLanesInputDto input)
    {
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        var (result, error) = await _plans.AutoLanesAsync(id, input);
        return result == null ? BadRequest(new { error }) : Ok(result);
    }

    /// <summary>
    /// Что мешает опубликовать (решение 10 плана trainings-coach-flow): идущие пловцы без уровня не
    /// на дорожке. Диалог публикации показывает их заранее; сам Publish проверяет то же.
    /// </summary>
    [HttpGet("{key}/publish-check")]
    public async Task<IActionResult> PublishCheck(int id, string key)
    {
        if (!LanePlanKey.TryParse(key, out var day)) return BadKey();
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        var check = await _plans.GetPublishCheckAsync(id, day);
        return check == null ? NotFound() : Ok(check);
    }

    /// <summary>
    /// Поставить пловца на дорожку его уровня (CF4 плана trainings-coach-flow): тренер задал уровень
    /// идущему в карточке человека. Ответ — сдвиг (<c>lane_change</c>) или <c>null</c>, если двигать
    /// не пришлось; 404 — у занятия плана нет.
    /// </summary>
    [HttpPost("{key}/swimmers/{swimmerId:int}/place")]
    public async Task<IActionResult> Place(int id, string key, int swimmerId)
    {
        if (!TryWriteKey(key, out var day, out var bad)) return bad!;
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        var change = await _plans.PlaceAsync(id, day, swimmerId, CurrentUserId()!.Value);
        return Ok(new { lane_change = change });
    }

    /// <summary>
    /// Опубликовать. Отказ 400 с текстом — решение 10. Тело необязательно; <c>notify</c> — push
    /// участникам «дорожки готовы» (только когда статус действительно сменился).
    /// </summary>
    [HttpPost("{key}/publish")]
    public async Task<IActionResult> Publish(
        int id, string key, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LanePlanPublishInputDto? input)
    {
        if (!TryWriteKey(key, out var day, out var bad)) return bad!;
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        var result = await _plans.PublishAsync(id, day, CurrentUserId()!.Value);
        if (!result.Found) return NotFound();
        if (result.Error != null) return BadRequest(new { error = result.Error });

        if (result.Changed && input?.Notify == true)
            await _notifier.NotifyPublishedAsync(id, day, CurrentUserId()!.Value, HttpContext.RequestAborted);
        return NoContent();
    }

    /// <summary>
    /// Лента «Since publishing» (CF8 плана trainings-coach-flow): что изменилось в опубликованном плане,
    /// новые сверху. Только управляющему — участникам лента не показывается. 404 — у занятия плана нет.
    /// </summary>
    [HttpGet("{key}/events")]
    public async Task<IActionResult> Events(int id, string key)
    {
        if (!LanePlanKey.TryParse(key, out var day)) return BadKey();
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        var events = await _plans.GetEventsAsync(id, day, CurrentUserId()!.Value);
        Response.Headers.CacheControl = "private, no-store";
        return events == null ? NotFound() : Ok(events);
    }

    [HttpPost("{key}/unpublish")]
    public Task<IActionResult> Unpublish(int id, string key) => SetStatusAsync(id, key, LanePlanStatus.Draft);

    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(int id, string key)
    {
        if (!TryWriteKey(key, out var day, out var bad)) return bad!;
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        return await _plans.DeleteAsync(id, day) ? NoContent() : NotFound();
    }

    private async Task<IActionResult> SetStatusAsync(int id, string key, string status)
    {
        if (!TryWriteKey(key, out var day, out var bad)) return bad!;
        if (await RequireCanEditAsync(id) is { } denied) return denied;

        return await _plans.SetStatusAsync(id, day, status, CurrentUserId()) ? NoContent() : NotFound();
    }
}
