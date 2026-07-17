using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.API.Http;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.API.Controllers;

/// <summary>
/// Публичные группы (HubGroups, фазы 3–4): список и страница группы с агрегатами.
/// Кэш — как у RecordsController (ETag + Cache-Control, инвалидация из админ-CRUD);
/// в ключ входит значение HubGroupVisibility, чтобы смена настройки не отдавала
/// устаревший состав списка (Update настройки кэш не инвалидирует).
/// Виртуальная группа «Моё избранное» — per-user, поэтому БЕЗ общего кэша.
/// </summary>
[ApiController]
public class HubGroupsController : ControllerBase
{
    private readonly IHubGroupPublicRepository _groups;
    private readonly ICacheService _cache;
    private readonly ISettingsService _settings;
    private readonly IHubGroupTrainingRepository _trainings;
    private readonly IHubGroupPermissionService _permissions;
    private readonly IResultRepository _results;
    private readonly IHubGroupMediaService _media;
    private readonly IUserMediaPublicationService _publications;

    private const string CacheControlValue = "public, max-age=60";
    private static readonly TimeSpan PayloadTtl = TimeSpan.FromMinutes(5);

    public HubGroupsController(
        IHubGroupPublicRepository groups, ICacheService cache, ISettingsService settings,
        IHubGroupTrainingRepository trainings, IHubGroupPermissionService permissions,
        IResultRepository results, IHubGroupMediaService media,
        IUserMediaPublicationService publications)
    {
        _publications = publications;
        _groups = groups;
        _cache = cache;
        _settings = settings;
        _trainings = trainings;
        _permissions = permissions;
        _results = results;
        _media = media;
    }

    private string Visibility => _settings.GetValue("HubGroupVisibility", "public");

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Список видимых групп.</summary>
    [HttpGet("/api/hub-groups")]
    public async Task<IActionResult> GetGroups()
        => await this.CachedJson(_cache, $"http:hub-groups:list:{Visibility}",
            () => _groups.GetGroupsAsync(), PayloadTtl, CacheControlValue);

    /// <summary>
    /// Виртуальная группа «Моё избранное» текущего пользователя — тот же контракт,
    /// что и у обычной группы. Литеральный маршрут стоит до {slug}, чтобы slug
    /// «favorites» не перехватывался как имя обычной группы.
    /// </summary>
    [HttpGet("/api/hub-groups/favorites")]
    [Authorize]
    public async Task<IActionResult> GetFavoritesGroup()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId)) return Unauthorized();

        // Личные данные — без общего кэша и без Cache-Control (пусть браузер не хранит).
        var dto = await _groups.GetFavoritesGroupAsync(userId);
        // У виртуальной группы галереи нет (Id=0) — лента соберётся из record/medals.
        dto.Highlights = HubGroupHighlightsBuilder.Build(dto);
        return Ok(dto);
    }

    /// <summary>Страница группы по slug: инфо, участники, последние заплывы, рекорды группы.</summary>
    [HttpGet("/api/hub-groups/{slug}")]
    public async Task<IActionResult> GetGroup(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 120)
            return BadRequest("slug is required");

        // 404/скрытость проверяем ДЁШЕВО (slug-lookup + ids), не строя payload: агрегаты
        // страницы (последние заплывы, рекорды группы, сезонный зачёт) ходят по Results
        // и на большой таблице стоят сотни мс — им место ТОЛЬКО внутри load-лямбды
        // CachedJson (выполняется на cache miss). 404 при этом не кэшируется.
        var roster = await _groups.GetRosterSwimmerIdsAsync(slug);
        if (roster == null) return NotFound();

        return await this.CachedJson(_cache,
            $"http:hub-groups:group:{slug.ToLowerInvariant()}:{Visibility}",
            async () =>
            {
                var dto = await _groups.GetBySlugAsync(slug)
                    // Группа исчезла между проверкой и загрузкой — гонка с удалением, не кэшируем мусор.
                    ?? throw new InvalidOperationException($"hub group '{slug}' vanished during load");

                // Публичная галерея (TrainingId == null) — через SwimmDbContext (не read-реплику):
                // у Sys_HubGroupMedia нет grant swimm_ro (см. SwimmDbContext.OnModelCreating).
                dto.Gallery = await _media.GetGalleryAsync(dto.Id);
                // Лента хайлайтов шапки — строго после заполнения Gallery (video/photo берутся из неё).
                dto.Highlights = HubGroupHighlightsBuilder.Build(dto);
                return dto;
            }, PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Результаты РОСТЕРА группы (вкладка «Competitions») — как /api/results, но заужено на
    /// участников группы (HubGroupMembers). Публично, кэшируемо, те же фильтры/пагинация.
    /// </summary>
    [HttpGet("/api/hub-groups/{slug}/results")]
    public async Task<IActionResult> GetGroupResults(
        string slug,
        [FromQuery] string? styleName,
        [FromQuery] string? distance,
        [FromQuery] string? gender,
        [FromQuery] string? poolType,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int? birthYearFrom,
        [FromQuery] int? birthYearTo,
        [FromQuery] string? ageGroup,
        [FromQuery] string? position,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 120)
            return BadRequest("slug is required");

        if (pageSize > 500) pageSize = 500;
        if (pageSize < 1) pageSize = 1;
        if (page < 1) page = 1;

        var rosterIds = await _groups.GetRosterSwimmerIdsAsync(slug);
        if (rosterIds is null) return NotFound();
        if (rosterIds.Count == 0) return Ok(new { page, pageSize, hasMore = false, total = 0, data = Array.Empty<object>() });

        (int? positionMax, var positionKeepUnranked) = position?.ToLowerInvariant() switch
        {
            null or "" or "all" => ((int?)null, false),
            "top" => (10, true),
            "podium" => (3, false),
            _ => (-1, false)
        };
        if (positionMax == -1)
            return BadRequest("position must be 'all', 'top' or 'podium'");

        var filter = new ResultFilter
        {
            SwimmerIds = rosterIds,
            StyleName = styleName,
            Distance = distance,
            Gender = gender,
            PoolType = poolType,
            DateFrom = dateFrom,
            DateTo = dateTo,
            BirthYearFrom = birthYearFrom,
            BirthYearTo = birthYearTo,
            AgeGroup = ageGroup,
            PositionMax = positionMax,
            PositionKeepUnranked = positionKeepUnranked
        };

        var (items, hasMore, total) = await _results.GetPagedAsync(filter, page, pageSize);
        return Ok(new { page, pageSize, hasMore, total, data = items });
    }

    /// <summary>
    /// ПРИВАТНЫЕ тренировки группы (вкладка «Тренировки»). Видят только владелец/админ группы
    /// или site-админ (CanEdit). Данные из Sys_-таблиц — БЕЗ кэша и без Cache-Control (личное).
    /// </summary>
    [HttpGet("/api/hub-groups/{slug}/trainings")]
    [Authorize]
    public async Task<IActionResult> GetGroupTrainings(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 120)
            return BadRequest("slug is required");

        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId)) return Unauthorized();

        var groupId = await _trainings.ResolveGroupIdBySlugAsync(slug);
        if (groupId is null) return NotFound();

        // Тренировки видят ВСЕ участники группы: управляющие (владелец/админ/админ-группы = CanEdit)
        // ИЛИ активный участник-аккаунт (Sys_HubGroupUserMembers). См. §4/§7.
        var perms = await _permissions.GetPermissionsAsync(groupId.Value, userId, User.IsInRole("Admin"));
        var isMember = await _trainings.IsActiveAccountMemberAsync(groupId.Value, userId);
        if (!perms.CanEdit && !isMember) return Forbid();

        return Ok(await _trainings.GetTrainingsAsync(groupId.Value));
    }

    /// <summary>
    /// Members-медиа группы (тренерские разборы, 2B′): Visibility=members вне тренировок,
    /// с контекстом якоря (пловец/заплыв). Видят активные участники-аккаунты группы и
    /// управляющие (CanEdit) — та же аудитория, что у тренировок. Без кэша (личное).
    /// </summary>
    [HttpGet("/api/hub-groups/{slug}/media/members")]
    [Authorize]
    public async Task<IActionResult> GetMembersMedia(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 120)
            return BadRequest("slug is required");

        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId)) return Unauthorized();

        var groupId = await _trainings.ResolveGroupIdBySlugAsync(slug);
        if (groupId is null) return NotFound();

        var perms = await _permissions.GetPermissionsAsync(groupId.Value, userId, User.IsInRole("Admin"));
        var isMember = await _trainings.IsActiveAccountMemberAsync(groupId.Value, userId);
        if (!perms.CanEdit && !isMember) return Forbid();

        return Ok(await _media.GetMembersMediaAsync(groupId.Value));
    }

    /// <summary>
    /// Добавить медиа группы (публичная галерея, если training_id не задан, иначе — медиа
    /// тренировки). Права — владелец/админ группы/site-админ (как в самообслуживании, CanEdit).
    /// </summary>
    [HttpPost("/api/hub-groups/{id:int}/media")]
    [Authorize]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> AddMedia(int id, [FromBody] HubGroupMediaInputDto input)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var perms = await _permissions.GetPermissionsAsync(id, userId.Value, User.IsInRole("Admin"));
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var result = await _media.AddAsync(id, input, userId.Value);
        if (!result.Success) return BadRequest(new { error = result.Error });

        // Галерея встроена в кэшируемый GET /api/hub-groups/{slug} — без инвалидации
        // владелец (и публика) до 5 минут видели бы старый список (идиома всех админ-мутаций).
        await _cache.InvalidateAllAsync();
        return Ok(new { id = result.Id });
    }

    /// <summary>Удалить медиа группы. 404, если запись не найдена в этой группе.</summary>
    [HttpDelete("/api/hub-groups/{id:int}/media/{mediaId:int}")]
    [Authorize]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> DeleteMedia(int id, int mediaId)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var perms = await _permissions.GetPermissionsAsync(id, userId.Value, User.IsInRole("Admin"));
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var removed = await _media.DeleteAsync(id, mediaId);
        if (!removed) return NotFound();

        await _cache.InvalidateAllAsync();
        return NoContent();
    }

    /* — Публикации личного медиа участников (этап 2 media-visibility-model) — */

    /// <summary>Inbox модерации публикаций (pending + approved). Только управляющие группой.</summary>
    [HttpGet("/api/hub-groups/{id:int}/media/publications")]
    [Authorize]
    public async Task<IActionResult> GetPublicationsInbox(int id)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var perms = await _permissions.GetPermissionsAsync(id, userId.Value, User.IsInRole("Admin"));
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        return Ok(await _publications.GetForGroupAsync(id));
    }

    /// <summary>
    /// Решение по заявке: approve=true → опубликовать, false → отклонить/снять с публикации.
    /// </summary>
    [HttpPost("/api/hub-groups/{id:int}/media/publications/{publicationId:int}/decision")]
    [Authorize]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> DecidePublication(int id, int publicationId, [FromBody] PublicationDecisionRequest request)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var perms = await _permissions.GetPermissionsAsync(id, userId.Value, User.IsInRole("Admin"));
        if (!perms.Exists) return NotFound();
        if (!perms.CanEdit) return Forbid();

        var ok = await _publications.DecideAsync(id, publicationId, request.Approve, userId.Value);
        if (!ok) return NotFound(new { error = "Publication not found" });

        return NoContent();
    }

    /// <summary>
    /// Одобренные публикации участников: level=public — любому посетителю; level=members —
    /// той же аудитории, что members-медиа (активный участник-аккаунт или CanEdit).
    /// </summary>
    [HttpGet("/api/hub-groups/{slug}/media/published")]
    public async Task<IActionResult> GetPublishedMedia(string slug, [FromQuery] string level = "public")
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 120)
            return BadRequest("slug is required");

        level = level.Trim().ToLowerInvariant();
        if (level != "public" && level != "members")
            return BadRequest(new { error = "level must be 'public' or 'members'" });

        var groupId = await _trainings.ResolveGroupIdBySlugAsync(slug);
        if (groupId is null) return NotFound();

        if (level == "members")
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var perms = await _permissions.GetPermissionsAsync(groupId.Value, userId.Value, User.IsInRole("Admin"));
            var isMember = await _trainings.IsActiveAccountMemberAsync(groupId.Value, userId.Value);
            if (!perms.CanEdit && !isMember) return Forbid();
        }

        return Ok(await _publications.GetApprovedForGroupAsync(groupId.Value, level));
    }
}
