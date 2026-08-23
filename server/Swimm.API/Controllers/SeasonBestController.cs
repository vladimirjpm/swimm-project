using Microsoft.AspNetCore.Mvc;
using Swimm.API.Http;
using Swimm.Application.Abstractions;

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
}
