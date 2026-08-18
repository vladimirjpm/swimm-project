using Microsoft.AspNetCore.Mvc;
using Swimm.API.Http;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// Публичные догрузки страницы клуба (K4.2, docs/plans/club-page-plan.md): ростер («Show all N»)
/// и клубные рекорды (переключатель бассейна 25m/50m). Карточку overview (K4.1) отдаёт
/// отдельный контроллер — здесь её нет и не будет.
///
/// Псевдоклуб (Club.IsPseudo — страна/сборная, не клуб) → 404. Склеенный клуб
/// (Club.MergedIntoId) → данные клуба-приёмника, без редиректа (решение Влада).
/// Резолв {id} — дёшево, ДО кэшируемой загрузки (как 404-проверка у /api/hub-groups/{slug}),
/// чтобы 404 и разные исходные id одного приёмника не плодили отдельные кэш-записи.
///
/// Кэш — как у RecordsController/HubGroupsController: ETag + Cache-Control, инвалидация —
/// общий ICacheService.InvalidateAllAsync после админ-мутаций/импорта/пересчёта зачёта.
/// </summary>
[ApiController]
public class ClubsPublicController : ControllerBase
{
    private readonly IClubPublicRepository _clubs;
    private readonly IClubOverviewRepository _overview;
    private readonly ICacheService _cache;

    private const string CacheControlValue = "public, max-age=60";
    private const int DefaultGridSeasons = 3;
    private const int MaxGridSeasons = 20;
    private static readonly TimeSpan PayloadTtl = TimeSpan.FromMinutes(5);

    public ClubsPublicController(
        IClubPublicRepository clubs, IClubOverviewRepository overview, ICacheService cache)
    {
        _clubs = clubs;
        _overview = overview;
        _cache = cache;
    }

    /// <summary>
    /// Сборный ответ страницы клуба: Hero, фильтры, грид «сезон × группа», таблица зачёта,
    /// история и топ пловцов. Ростер и рекорды — отдельными эндпоинтами ниже.
    /// </summary>
    [HttpGet("/api/clubs/{id:int}/overview")]
    public async Task<IActionResult> GetOverview(
        int id,
        [FromQuery] int? season = null,
        [FromQuery] string? group = null,
        [FromQuery] int gridSeasons = DefaultGridSeasons,
        [FromQuery(Name = "standing")] int? standingCompetitionId = null)
    {
        // Верхняя граница — не каприз: сезонов 20+ и число растёт, а грид при «все сезоны»
        // отдаёт по строке на каждую зачётную группу каждого сезона.
        if (gridSeasons < 1) gridSeasons = 1;
        if (gridSeasons > MaxGridSeasons) gridSeasons = MaxGridSeasons;

        var resolvedId = await _clubs.ResolveClubIdAsync(id);
        if (resolvedId == null) return NotFound();

        return await this.CachedJson(_cache,
            $"http:clubs:{resolvedId}:overview:{season?.ToString() ?? "all"}:{group ?? "all"}:{gridSeasons}:{standingCompetitionId?.ToString() ?? "auto"}",
            () => _overview.GetOverviewAsync(
                resolvedId.Value, id, season, group, gridSeasons, standingCompetitionId),
            PayloadTtl, CacheControlValue);
    }

    /// <summary>Ростер клуба: пагинация + фильтры пол/возраст/сезон.</summary>
    [HttpGet("/api/clubs/{id:int}/roster")]
    public async Task<IActionResult> GetRoster(
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? gender = null,
        [FromQuery] int? ageFrom = null,
        [FromQuery] int? ageTo = null,
        [FromQuery] int? season = null)
    {
        if (gender != null && gender != "male" && gender != "female")
            return BadRequest("gender must be 'male' or 'female'");

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;

        var resolvedId = await _clubs.ResolveClubIdAsync(id);
        if (resolvedId == null) return NotFound();

        return await this.CachedJson(_cache,
            $"http:clubs:{resolvedId}:roster:{page}:{pageSize}:{gender ?? "all"}:{ageFrom?.ToString() ?? "-"}:{ageTo?.ToString() ?? "-"}:{season?.ToString() ?? "cur"}",
            () => _clubs.GetRosterAsync(resolvedId.Value, page, pageSize, gender, ageFrom, ageTo, season),
            PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Season best: лучшие времена клуба за ОДИН сезон, разложенные по возрастным ступеням
    /// внутри дисциплины (по нашим протоколам). pool — фильтр 25m/50m, season — год начала
    /// сезона; без него берётся ТЕКУЩИЙ сезон (карточка сознательно не умеет «за всё время»).
    /// </summary>
    [HttpGet("/api/clubs/{id:int}/season-best")]
    public async Task<IActionResult> GetSeasonBest(
        int id, [FromQuery] string? pool = null, [FromQuery] int? season = null)
    {
        if (pool != null && pool != "25m" && pool != "50m")
            return BadRequest("pool must be '25m' or '50m'");

        var resolvedId = await _clubs.ResolveClubIdAsync(id);
        if (resolvedId == null) return NotFound();

        return await this.CachedJson(_cache,
            $"http:clubs:{resolvedId}:season-best:{pool ?? "all"}:{season?.ToString() ?? "cur"}",
            () => _clubs.GetSeasonBestAsync(resolvedId.Value, pool, season),
            PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Стена официальных рекордов клуба (таблица Records: нац./возрастные/мастерс/мировые).
    /// Сезона тут нет сознательно: рекорд действует, пока его не побили, а не «в сезоне».
    /// </summary>
    [HttpGet("/api/clubs/{id:int}/record-wall")]
    public async Task<IActionResult> GetRecordWall(int id, [FromQuery] string? pool = null)
    {
        if (pool != null && pool != "25m" && pool != "50m")
            return BadRequest("pool must be '25m' or '50m'");

        var resolvedId = await _clubs.ResolveClubIdAsync(id);
        if (resolvedId == null) return NotFound();

        return await this.CachedJson(_cache,
            $"http:clubs:{resolvedId}:record-wall:{pool ?? "all"}",
            () => _clubs.GetRecordWallAsync(resolvedId.Value, pool),
            PayloadTtl, CacheControlValue);
    }
}
