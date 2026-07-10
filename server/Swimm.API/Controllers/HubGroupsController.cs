using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.API.Http;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

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

    private const string CacheControlValue = "public, max-age=60";
    private static readonly TimeSpan PayloadTtl = TimeSpan.FromMinutes(5);

    public HubGroupsController(
        IHubGroupPublicRepository groups, ICacheService cache, ISettingsService settings,
        IHubGroupTrainingRepository trainings, IHubGroupPermissionService permissions,
        IResultRepository results)
    {
        _groups = groups;
        _cache = cache;
        _settings = settings;
        _trainings = trainings;
        _permissions = permissions;
        _results = results;
    }

    private string Visibility => _settings.GetValue("HubGroupVisibility", "public");

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
        return Ok(await _groups.GetFavoritesGroupAsync(userId));
    }

    /// <summary>Страница группы по slug: инфо, участники, последние заплывы, рекорды группы.</summary>
    [HttpGet("/api/hub-groups/{slug}")]
    public async Task<IActionResult> GetGroup(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 120)
            return BadRequest("slug is required");

        // Грузим до CachedJson: 404 кэшировать нельзя (хелпер пишет payload безусловно).
        // БД-запрос идёт на каждый hit, кэш здесь даёт ETag/304 и экономию сериализации.
        var dto = await _groups.GetBySlugAsync(slug);
        if (dto == null) return NotFound();

        return await this.CachedJson(_cache,
            $"http:hub-groups:group:{slug.ToLowerInvariant()}:{Visibility}",
            () => Task.FromResult(dto), PayloadTtl, CacheControlValue);
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
}
