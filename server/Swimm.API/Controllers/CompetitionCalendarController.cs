using Microsoft.AspNetCore.Mvc;
using Swimm.API.Http;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Domain;

namespace Swimm.API.Controllers;

/// <summary>
/// Календарь сезона — главная <c>/competitions</c> (docs/plans/competitions-home-plan.md, часть Б).
/// Отдельный эндпоинт, а не расширение <c>/api/competitions</c>: тот грузит селектор results
/// целиком на каждой странице результатов, а календарю нужен один сезон с заявками и площадкой —
/// смешай их, и каждая страница результатов платила бы за «Входящие» и стартовые протоколы.
/// </summary>
[ApiController]
public class CompetitionCalendarController : ControllerBase
{
    private const string CacheControlValue = "public, max-age=60";
    private static readonly TimeSpan PayloadTtl = TimeSpan.FromMinutes(5);

    private readonly ICompetitionCalendarRepository _calendar;
    private readonly ICacheService _cache;

    public CompetitionCalendarController(ICompetitionCalendarRepository calendar, ICacheService cache)
    {
        _calendar = calendar;
        _cache = cache;
    }

    /// <summary>
    /// Старты одного сезона (сентябрь–август). <paramref name="season"/> — год начала;
    /// не задан — текущий по дате Израиля.
    /// </summary>
    [HttpGet("/api/competitions/calendar")]
    public async Task<IActionResult> GetSeason([FromQuery] int? season, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(IsraelTime.ToLocal(DateTime.UtcNow));
        var year = season ?? SeasonMath.StartYearOf(today.ToDateTime(TimeOnly.MinValue));
        if (year is < 1990 or > 2100) return BadRequest("season must be a start year, e.g. 2026");

        // Дата в ключе: статусы (live/upcoming/done) считаются от «сегодня» и не должны пережить полночь.
        return await this.CachedJson(_cache,
            $"http:competition-calendar:{year}:{today:yyyy-MM-dd}",
            () => _calendar.GetSeasonAsync(year, today, ct),
            PayloadTtl, CacheControlValue);
    }
}
