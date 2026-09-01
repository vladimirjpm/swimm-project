using Microsoft.AspNetCore.Mvc;
using Swimm.API.Http;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Controllers;

/// <summary>
/// Национальный season best одной дисциплины — таб рядом с возрастными рекордами
/// (design_handoff_age_records_sb).
///
/// Кэшируется ровно как рекорды (<see cref="RecordsController"/>): сериализованный JSON и его
/// ETag лежат в ICacheService сутки, браузеру — Cache-Control max-age=300 + ETag (повтор с
/// If-None-Match → 304 без тела). Инвалидация глобальная (ICacheService.InvalidateAllAsync
/// после импорта и админ-мутаций), поэтому после нового протокола витрина обновится сама.
/// </summary>
[ApiController]
public class SeasonBestController : ControllerBase
{
    private readonly ISeasonBestRepository _seasonBest;
    private readonly ICacheService _cache;

    private const string CacheControlValue = "public, max-age=300";
    private static readonly TimeSpan PayloadTtl = TimeSpan.FromHours(24);

    public SeasonBestController(ISeasonBestRepository seasonBest, ICacheService cache)
    {
        _seasonBest = seasonBest;
        _cache = cache;
    }

    /// <summary>
    /// Лучшее время сезона по стране в каждой паре «пол × возраст в сезоне».
    /// style — как в Styles.Name (freestyle/backstroke/…), distance — как в Results.Distance
    /// («50», «100», без «m»), pool — 25m/50m (опционально), season — год НАЧАЛА сезона
    /// (опционально; по умолчанию текущий).
    /// </summary>
    [HttpGet("/api/season-best")]
    public async Task<IActionResult> GetSeasonBest(
        [FromQuery] string? style,
        [FromQuery] string? distance,
        [FromQuery] string? pool = null,
        [FromQuery] int? season = null)
    {
        if (string.IsNullOrWhiteSpace(style)) return BadRequest("style is required");
        if (string.IsNullOrWhiteSpace(distance)) return BadRequest("distance is required");
        if (pool != null && pool != "25m" && pool != "50m")
            return BadRequest("pool must be '25m' or '50m'");

        var styleKey = style.Trim();
        // «50m» от витрины и «50» из БД — одна дистанция; нормализуем на входе, чтобы
        // ключ кэша не двоился.
        var distanceKey = distance.Trim().TrimEnd('m', 'M');

        return await this.CachedJson(_cache,
            $"http:season-best:{styleKey}:{distanceKey}:{pool ?? "all"}:{season?.ToString() ?? "cur"}",
            () => _seasonBest.GetNationalSeasonBestAsync(
                styleKey, distanceKey, pool, season, HttpContext.RequestAborted),
            PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Ранжированный список одной дисциплины за сезон — страница <c>/season-best</c>.
    ///
    /// Параметры — зеркало query этой страницы (<c>client/src/utils/routes.ts</c>): один и тот
    /// же адрес и открывает экран, и наполняет его.
    ///
    /// По умолчанию отдаются ВСЕ заплывы среза, а не по одному на пловца: один человек законно
    /// занимает и первое место, и третье — это его разные старты за сезон. Схлопывание
    /// включается флагом <paramref name="best"/>.
    /// </summary>
    [HttpGet("/api/season-best/list")]
    public async Task<IActionResult> GetSeasonBestList(
        [FromQuery] string? style,
        [FromQuery] string? distance,
        [FromQuery] string? pool = null,
        [FromQuery] int? season = null,
        [FromQuery] int? age = null,
        [FromQuery(Name = "age_to")] int? ageTo = null,
        [FromQuery] string? gender = null,
        [FromQuery(Name = "club")] int? clubId = null,
        [FromQuery] bool masters = false,
        [FromQuery(Name = "age_group")] string? ageGroup = null,
        [FromQuery] bool best = false,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        if (string.IsNullOrWhiteSpace(style)) return BadRequest("style is required");
        if (string.IsNullOrWhiteSpace(distance)) return BadRequest("distance is required");
        if (pool != null && pool != "25m" && pool != "50m")
            return BadRequest("pool must be '25m' or '50m'");

        var genderKey = gender?.Trim().ToLowerInvariant();
        if (genderKey is not (null or "" or "male" or "female"))
            return BadRequest("gender must be 'male' or 'female'");

        var query = new SeasonBestListQuery
        {
            Style = style.Trim(),
            Distance = distance.Trim().TrimEnd('m', 'M'),
            PoolType = pool,
            Season = season,
            Age = age,
            AgeTo = ageTo,
            Gender = string.IsNullOrEmpty(genderKey) ? null : genderKey,
            ClubId = clubId,
            // Группа приходит только вместе с мастерским срезом: в обычных стартах ось
            // возраста другая, и молча принятая группа дала бы пустой список без объяснения.
            Masters = masters,
            AgeGroup = masters ? ageGroup?.Trim() : null,
            BestPerSwimmer = best,
            Limit = limit,
            Offset = offset,
        };

        // Ключ кэша перечисляет ВСЕ параметры среза: пропустишь один — и два разных списка
        // начнут отдавать один и тот же ответ.
        var key = $"http:season-best:list:{query.Style}:{query.Distance}:{pool ?? "all"}:"
                  + $"{season?.ToString() ?? "cur"}:{age?.ToString() ?? "-"}:{ageTo?.ToString() ?? "-"}:"
                  + $"{query.Gender ?? "all"}:{clubId?.ToString() ?? "all"}:{best}:{limit}:{offset}:"
                  + $"{masters}:{query.AgeGroup ?? "-"}";

        return await this.CachedJson(_cache, key,
            () => _seasonBest.GetSeasonBestListAsync(query, HttpContext.RequestAborted),
            PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Опции страницы: сезоны с данными (карусель) и стили с реально проплытыми дистанциями
    /// (селектор дисциплины). Меняются только после импорта, поэтому кэш общий и без параметров.
    /// </summary>
    [HttpGet("/api/season-best/options")]
    public async Task<IActionResult> GetSeasonBestOptions()
    {
        return await this.CachedJson(_cache, "http:season-best:options",
            () => _seasonBest.GetSeasonBestOptionsAsync(HttpContext.RequestAborted),
            PayloadTtl, CacheControlValue);
    }
}
