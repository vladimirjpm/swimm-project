using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Validation;
using Swimm.Domain.Entities;

namespace Swimm.API.Controllers;

/// <summary>
/// 2A: личное owner-only медиа пловца (ссылки youtube/vimeo/other). Публичного слоя
/// нет — см. docs/tasks/user-media-2a-sonnet.md (2B добавит /api/media + visibility=public).
/// </summary>
[ApiController]
[Route("api/me/media")]
[Authorize]
[AutoValidateAntiforgeryToken]
public class MediaController : ControllerBase
{
    private readonly IUserMediaRepository _media;
    private readonly IMySwimsRepository _mySwims;
    private readonly IUserMediaPublicationService _publications;
    private readonly IHubGroupPermissionService _groupPermissions;
    private readonly ICacheService _cache;

    public MediaController(
        IUserMediaRepository media,
        IMySwimsRepository mySwims,
        IUserMediaPublicationService publications,
        IHubGroupPermissionService groupPermissions,
        ICacheService cache)
    {
        _media = media;
        _mySwims = mySwims;
        _publications = publications;
        _groupPermissions = groupPermissions;
        _cache = cache;
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    [HttpGet]
    public async Task<IActionResult> GetMedia([FromQuery] int? swimmerId)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        return Ok(await _media.GetForUserAsync(userId.Value, swimmerId));
    }

    /// <summary>
    /// My media v3: заплывы favorite-пловцов за сезон (сентябрь–август, ?season=стартовый год,
    /// дефолт — текущий) с медиа, реакциями и PB-флагами + competition-level и unlinked медиа.
    /// </summary>
    [HttpGet("/api/me/swims")]
    public async Task<IActionResult> GetMySwims([FromQuery] string? season)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        // «all» — все сезоны сразу; иначе год начала сезона. Санити: значения вне разумного
        // окна режем до дефолта (витринный сезон, его выбирает репозиторий).
        var allSeasons = string.Equals(season, "all", StringComparison.OrdinalIgnoreCase);
        int? seasonYear = !allSeasons && int.TryParse(season, out var y) && y is >= 1990 and <= 2100 ? y : null;

        return Ok(await _mySwims.GetMySwimsAsync(userId.Value, seasonYear, allSeasons));
    }

    /// <summary>Потолок медиа на пользователя — страховка от замусоривания таблицы ботом.</summary>
    private const int MaxMediaPerUser = 500;

    [HttpPost]
    [EnableRateLimiting("media")]
    public async Task<IActionResult> AddMedia([FromBody] AddUserMediaRequest request)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        if (request.SwimmerId <= 0)
            return BadRequest(new { error = "swimmer_id is required" });

        if (await _media.CountForUserAsync(userId.Value) >= MaxMediaPerUser)
            return BadRequest(new { error = "media limit reached" });

        if (!MediaUrlValidator.TryValidate(request.MediaType, request.SourceType, request.Url, out var error))
            return BadRequest(new { error });

        var media = await _media.AddAsync(userId.Value, request);
        if (media == null)
            return BadRequest(new { error = "Swimmer not found" });

        return CreatedAtAction(nameof(GetMedia), media);
    }

    /* — Публикации в группы (этап 2 media-visibility-model) — */

    /// <summary>Публикации всех моих медиа (статусы заявок для «Моих ссылок»).</summary>
    [HttpGet("publications")]
    public async Task<IActionResult> GetMyPublications()
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        return Ok(await _publications.GetForOwnerAsync(userId.Value));
    }

    /// <summary>
    /// Сводный inbox модерации (My media → Moderation): заявки по всем группам, где я
    /// владелец/админ; site admin видит все группы. Решения — существующим
    /// POST /api/hub-groups/{id}/media/publications/{pubId}/decision (hub_group_id в строках).
    /// </summary>
    [HttpGet("/api/me/moderation/media")]
    public async Task<IActionResult> GetModerationFeed()
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        return Ok(await _publications.GetModerationFeedAsync(userId.Value, User.IsInRole("Admin")));
    }

    /// <summary>Куда можно подать это медиа (я член/владелец/админ группы + пловец в ростере).</summary>
    [HttpGet("{id:int}/publish-targets")]
    public async Task<IActionResult> GetPublishTargets(int id)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        return Ok(await _publications.GetPublishTargetsAsync(userId.Value, id, User.IsInRole("Admin")));
    }

    /// <summary>Подать медиа в группу (level: members|public). Админ группы — сразу approved.</summary>
    [HttpPost("{id:int}/publications")]
    [EnableRateLimiting("media")]
    public async Task<IActionResult> SubmitPublication(int id, [FromBody] SubmitPublicationRequest request)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        // Кто «и так решает по этой цели» — тот подаёт сразу в approved, минуя inbox.
        // У группы это владелец/админ группы/site-админ; у клуба управляющих не существует,
        // поэтому только админ сайта, а обычная заявка ложится pending (план §3.10).
        bool privileged;
        if (request.TargetType == UserMediaPublicationTarget.Club)
        {
            privileged = User.IsInRole("Admin");
        }
        else
        {
            var perms = await _groupPermissions.GetPermissionsAsync(
                request.TargetId, userId.Value, User.IsInRole("Admin"));
            if (!perms.Exists) return BadRequest(new { error = "group not found" });
            privileged = perms.CanEdit;
        }

        var (success, error, publication) = await _publications.SubmitAsync(
            userId.Value, id, request, privileged);
        if (!success) return BadRequest(new { error });

        // Авто-approve привилегированной подачи сразу меняет публичную витрину группы
        // (Gallery/Highlights в кэшируемом payload страницы группы).
        if (publication!.Status == "approved") await _cache.InvalidateAllAsync();
        return Ok(publication);
    }

    /// <summary>Отозвать публикацию своего медиа из группы (любой статус).</summary>
    [HttpDelete("{id:int}/publications/{targetType}/{targetId:int}")]
    public async Task<IActionResult> WithdrawPublication(int id, string targetType, int targetId)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var ok = await _publications.WithdrawAsync(userId.Value, id, targetType, targetId);
        if (!ok) return NotFound(new { error = "Publication not found" });

        // Отзыв approved public-публикации убирает её из витрины группы — сброс кэша страницы.
        await _cache.InvalidateAllAsync();
        return NoContent();
    }

    /// <summary>
    /// Публичная лента медиа КЛУБА — одобренные public-публикации с целью-клубом.
    ///
    /// Ростер клуба приходит из справочника федерации, поэтому отдельного состава вести не
    /// нужно: сюда попадает всё, что подали и одобрили по пловцам этого клуба. Уровня
    /// members у клуба не бывает — членства как аккаунта у него нет (план §3.10).
    /// </summary>
    [HttpGet("/api/clubs/{clubId:int}/media")]
    [AllowAnonymous]
    public async Task<IActionResult> GetClubMedia(int clubId)
        => Ok(await _publications.GetApprovedForClubAsync(clubId));

    /// <summary>
    /// Решение по клубной заявке. Управляющих у клуба не существует, поэтому решает админ
    /// сайта; появятся («claim your club») — сюда добавится их проверка, и больше ничего.
    /// </summary>
    [HttpPost("/api/clubs/{clubId:int}/media/publications/{publicationId:int}/decision")]
    public async Task<IActionResult> DecideClubPublication(
        int clubId, int publicationId, [FromBody] PublicationDecisionRequest request)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();
        if (!User.IsInRole("Admin")) return Forbid();

        var ok = await _publications.DecideAsync(
            UserMediaPublicationTarget.Club, clubId, publicationId, request.Approve, userId.Value);
        if (!ok) return NotFound(new { error = "Publication not found" });

        // Лента клуба входит в кэшируемую страницу клуба — без сброса решение не видно.
        await _cache.InvalidateAllAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> RemoveMedia(int id)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var ok = await _media.RemoveAsync(userId.Value, id);
        return ok ? NoContent() : NotFound(new { error = "Media not found" });
    }
}
