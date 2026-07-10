using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.API.Http;
using Swimm.Application.Abstractions;

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

    private const string CacheControlValue = "public, max-age=60";
    private static readonly TimeSpan PayloadTtl = TimeSpan.FromMinutes(5);

    public HubGroupsController(IHubGroupPublicRepository groups, ICacheService cache, ISettingsService settings)
    {
        _groups = groups;
        _cache = cache;
        _settings = settings;
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
}
