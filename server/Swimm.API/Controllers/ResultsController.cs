using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ResultsController : ControllerBase
{
    private readonly IResultRepository _results;

    public ResultsController(IResultRepository results)
    {
        _results = results;
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
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        if (pageSize > 500) pageSize = 500;

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
            DateFrom = dateFrom,
            DateTo = dateTo
        };

        var (items, hasMore) = await _results.GetPagedAsync(filter, page, pageSize);

        return Ok(new { page, pageSize, hasMore, data = items });
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
    public async Task<IActionResult> GetSources()
        => Ok(await _results.GetSourcesAsync());

    /// <summary>
    /// Карьерные (all-time) данные спортсмена для карточки: соревнования, заплывы,
    /// сумма очков, медали, лучшие времена по стилям. Пловец не найден → нулевой DTO.
    /// </summary>
    [HttpGet("/api/athletes/career")]
    public async Task<IActionResult> GetAthleteCareer([FromQuery] string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("name is required");
        var career = await _results.GetAthleteCareerAsync(name);
        return Ok(career ?? new AthleteCareerDto());
    }
}
