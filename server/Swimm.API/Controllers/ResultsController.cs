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
        [FromQuery] int? competitionId,
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

        var filter = new ResultFilter
        {
            Competition = competition,
            EventId = eventId,
            CompetitionId = competitionId,
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
}
