using Microsoft.AspNetCore.Mvc;
using Swimm.API.Http;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ResultsController : ControllerBase
{
    private readonly IResultRepository _results;
    private readonly ICacheService _cache;

    private const string CacheControlValue = "public, max-age=60";
    private static readonly TimeSpan PayloadTtl = TimeSpan.FromMinutes(5);

    public ResultsController(IResultRepository results, ICacheService cache)
    {
        _results = results;
        _cache = cache;
    }

    /// <summary>
    /// Получить результаты с фильтрацией и пагинацией.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetResults(
        [FromQuery] string? competition,
        [FromQuery] int? eventId,
        [FromQuery] string? competitionId,
        [FromQuery] string? name,
        [FromQuery] string? club,
        [FromQuery] string? styleName,
        [FromQuery] string? distance,
        [FromQuery] string? gender,
        [FromQuery] string? poolType,
        [FromQuery] string? country,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int? birthYearFrom,
        [FromQuery] int? birthYearTo,
        [FromQuery] string? ageGroup,
        [FromQuery] string? position,
        [FromQuery] string? eventDate,
        [FromQuery] int? swimmerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        if (pageSize > 500) pageSize = 500;
        if (pageSize < 1) pageSize = 1;
        if (page < 1) page = 1;

        // OFFSET в Postgres линейный — глубокое листание запрещаем, пользователь сужает фильтр.
        if ((long)page * pageSize > 10_000)
            return BadRequest("page too deep: page*pageSize must be <= 10000, narrow the filter");

        // position: all|top|podium → верхняя граница места.
        // top оставляет строки без места (DSQ/DNS) — зеркало клиентского фильтра; podium — нет.
        (int? positionMax, var positionKeepUnranked) = position?.ToLowerInvariant() switch
        {
            null or "" or "all" => ((int?)null, false),
            "top" => (10, true),
            "podium" => (3, false),
            _ => (-1, false)
        };
        if (positionMax == -1)
            return BadRequest("position must be 'all', 'top' or 'podium'");

        // eventDate: конкретный день события, dd/MM/yyyy (формат Competition.Date)
        DateTime? eventDateValue = null;
        if (!string.IsNullOrWhiteSpace(eventDate))
        {
            if (!DateTime.TryParseExact(eventDate, "dd/MM/yyyy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedDate))
                return BadRequest("eventDate must be in dd/MM/yyyy format");
            eventDateValue = parsedDate;
        }

        // competitionId: число или "last" (последнее по дате соревнование/событие)
        int? competitionIdValue = null;
        var latest = false;
        if (!string.IsNullOrWhiteSpace(competitionId))
        {
            if (string.Equals(competitionId, "last", StringComparison.OrdinalIgnoreCase))
                latest = true;
            else if (int.TryParse(competitionId, out var parsed))
                competitionIdValue = parsed;
            else
                return BadRequest("competitionId must be a number or 'last'");
        }

        var filter = new ResultFilter
        {
            Competition = competition,
            EventId = eventId,
            CompetitionId = competitionIdValue,
            Latest = latest,
            Name = name,
            Club = club,
            StyleName = styleName,
            Distance = distance,
            Gender = gender,
            PoolType = poolType,
            Country = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant(),
            DateFrom = dateFrom,
            DateTo = dateTo,
            BirthYearFrom = birthYearFrom,
            BirthYearTo = birthYearTo,
            AgeGroup = ageGroup,
            PositionMax = positionMax,
            PositionKeepUnranked = positionKeepUnranked,
            EventDate = eventDateValue,
            SwimmerIds = swimmerId != null ? [swimmerId.Value] : null
        };

        var (items, hasMore, total) = await _results.GetPagedAsync(filter, page, pageSize);

        return Ok(new { page, pageSize, hasMore, total, data = items });
    }

    // Публичный аналог админского /api/admin/results/filter-hints: в paged-режиме клиент
    // не имеет полного датасета и берёт опции фильтров отсюда (контракт 3.2 §4).
    private static readonly HashSet<string> AllowedHintFields =
        new(["style", "distance", "club", "competition", "name"]);

    /// <summary>Подсказки значений фильтров (prefix-поиск, кэш на сервере).</summary>
    [HttpGet("filter-hints")]
    public async Task<IActionResult> GetFilterHints(
        [FromQuery] string field,
        [FromQuery] string? q,
        [FromQuery] int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(field) || !AllowedHintFields.Contains(field))
            return BadRequest("field must be one of: style, distance, club, competition, name");

        return await this.CachedJson(_cache, $"http:filter-hints:{field}:{q}:{limit}",
            () => _results.GetFilterHintsAsync(field, q, limit), PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Получить один результат по ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(long id)
    {
        var result = await _results.GetByIdAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Список источников для клиентского DDL: многодневные события (свёрнуты в одну запись) +
    /// однодневные соревнования. Грузить результаты источника: /api/results?eventId= или ?competitionId=.
    /// </summary>
    [HttpGet("/api/competitions")]
    public async Task<IActionResult> GetSources([FromQuery] string? country)
    {
        var countryKey = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant();

        return await this.CachedJson(_cache,
            $"http:competition-sources:{countryKey ?? "all"}",
            () => _results.GetSourcesAsync(countryKey), PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Сводка по клубам для источника (очки/медали/пловцы) — в paged-режиме клиент не имеет
    /// полного датасета, чтобы посчитать её сам (фаза 3.4). Область = тот же источник, что
    /// у результатов: competitionId (число или 'last') / eventId / country.
    /// </summary>
    [HttpGet("/api/club-summary")]
    public async Task<IActionResult> GetClubSummary(
        [FromQuery] string? competitionId,
        [FromQuery] int? eventId,
        [FromQuery] string? country,
        [FromQuery] string? poolType,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] bool? combined)
    {
        int? competitionIdValue = null;
        var latest = false;
        if (!string.IsNullOrWhiteSpace(competitionId))
        {
            if (string.Equals(competitionId, "last", StringComparison.OrdinalIgnoreCase))
                latest = true;
            else if (int.TryParse(competitionId, out var parsed))
                competitionIdValue = parsed;
            else
                return BadRequest("competitionId must be a number or 'last'");
        }

        var filter = new ResultFilter
        {
            CompetitionId = competitionIdValue,
            Latest = latest,
            EventId = eventId,
            Country = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant(),
            PoolType = poolType,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Combined = combined == true
        };

        return await this.CachedJson(_cache,
            $"http:club-summary:{filter.CompetitionId}:{filter.Latest}:{filter.EventId}:{filter.Country}:{filter.PoolType}:{filter.DateFrom:yyyyMMdd}:{filter.DateTo:yyyyMMdd}:{filter.Combined}",
            () => _results.GetClubSummaryAsync(filter), PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Дэшборд соревнования для таба Overview (сводка, дни, лучший заплыв, топ-клубы общий и
    /// по полу, топ-медалист). Область = тот же источник, что у результатов:
    /// competitionId (число или 'last') / eventId.
    /// </summary>
    [HttpGet("/api/competition-overview")]
    /// <param name="combined">Считать по объединённым местам «Combine All Results»
    /// (тоггл на клиенте). null/0 — по протокольным, как в таблице с выключенным тогглом.
    /// Действует только у соревнований с ShowCombineAllResults.</param>
    public async Task<IActionResult> GetCompetitionOverview(
        [FromQuery] string? competitionId,
        [FromQuery] int? eventId,
        [FromQuery] bool? combined)
    {
        int? competitionIdValue = null;
        var latest = false;
        if (!string.IsNullOrWhiteSpace(competitionId))
        {
            if (string.Equals(competitionId, "last", StringComparison.OrdinalIgnoreCase))
                latest = true;
            else if (int.TryParse(competitionId, out var parsed))
                competitionIdValue = parsed;
            else
                return BadRequest("competitionId must be a number or 'last'");
        }

        if (competitionIdValue is null && !latest && eventId is null)
            return BadRequest("competitionId or eventId is required");

        var filter = new ResultFilter
        {
            CompetitionId = competitionIdValue,
            Latest = latest,
            EventId = eventId,
            Combined = combined == true
        };

        return await this.CachedJson(_cache,
            $"http:competition-overview:{filter.CompetitionId}:{filter.Latest}:{filter.EventId}:{filter.Combined}",
            () => _results.GetCompetitionOverviewAsync(filter), PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Карьерные (all-time) данные спортсмена для карточки: соревнования, заплывы,
    /// сумма очков, медали, лучшие времена по стилям. Пловец не найден → нулевой DTO.
    /// </summary>
    [HttpGet("/api/athletes/career")]
    public async Task<IActionResult> GetAthleteCareer([FromQuery] string? name, [FromQuery] int? id)
    {
        // id — основной ключ (страница спортсмена), name — алиас для попапа-карточки.
        // Имена не уникальны: у тёзок именной путь склеит двух разных людей в одну карьеру.
        if (id is > 0)
            return await this.CachedJson(_cache, $"http:athlete-career-id:{id}",
                async () => await _results.GetAthleteCareerByIdAsync(id.Value) ?? new AthleteCareerDto(),
                PayloadTtl, CacheControlValue);

        if (string.IsNullOrWhiteSpace(name)) return BadRequest("name or id is required");

        return await this.CachedJson(_cache, $"http:athlete-career:{name.Trim().ToLowerInvariant()}",
            async () => await _results.GetAthleteCareerAsync(name) ?? new AthleteCareerDto(),
            PayloadTtl, CacheControlValue);
    }

}
