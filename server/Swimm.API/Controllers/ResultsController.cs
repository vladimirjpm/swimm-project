using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swimm.Infrastructure.Data;
using Swimm.Application.Mapping;

namespace Swimm.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ResultsController : ControllerBase
{
    private readonly SwimmDbContext _db;

    public ResultsController(SwimmDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Получить результаты с фильтрацией и пагинацией.
    /// Данные читаются через JOIN на справочники.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetResults(
        [FromQuery] string? competition,
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

        var query = _db.Results.AsNoTracking().AsQueryable();

        // Exact match по inline-полям (использует индексы)
        if (!string.IsNullOrWhiteSpace(styleName))
            query = query.Where(r => r.Style.Name == styleName);

        if (!string.IsNullOrWhiteSpace(distance))
            query = query.Where(r => r.Distance == distance);

        if (!string.IsNullOrWhiteSpace(gender))
            query = query.Where(r => r.Gender == gender);

        if (!string.IsNullOrWhiteSpace(poolType))
            query = query.Where(r => r.Competition.PoolType == poolType);

        // Date range (DateTime, использует индекс)
        if (dateFrom.HasValue)
            query = query.Where(r => r.CompetitionDate >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(r => r.CompetitionDate <= dateTo.Value);

        // LIKE-фильтры через навигационные свойства
        if (!string.IsNullOrWhiteSpace(competition))
            query = query.Where(r => r.Competition.Name.StartsWith(competition));

        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(r =>
                r.Swimmer.LastName.StartsWith(name) ||
                r.Swimmer.FirstName.StartsWith(name) ||
                r.Swimmer.LastNameEn.StartsWith(name) ||
                r.Swimmer.FirstNameEn.StartsWith(name));

        if (!string.IsNullOrWhiteSpace(club))
            query = query.Where(r => r.Club.Name.StartsWith(club) || r.Club.NameEn.StartsWith(club));

        // Берём pageSize + 1 чтобы определить hasMore без COUNT
        var results = await query
            .OrderByDescending(r => r.CompetitionDate)
            .ThenBy(r => r.Position)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .Select(ResultMapping.ToDto)
            .ToListAsync();

        var hasMore = results.Count > pageSize;
        if (hasMore)
            results.RemoveAt(results.Count - 1);

        return Ok(new
        {
            page,
            pageSize,
            hasMore,
            data = results
        });
    }

    /// <summary>
    /// Получить один результат по ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(long id)
    {
        var r = await _db.Results.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(ResultMapping.ToDto)
            .FirstOrDefaultAsync();

        if (r == null) return NotFound();
        return Ok(r);
    }
}
