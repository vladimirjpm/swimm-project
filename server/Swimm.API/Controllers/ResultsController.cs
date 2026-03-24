using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swimm.API.Data;
using Swimm.API.Models;

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
            .Select(r => new ResultDto
            {
                Id = r.Id,
                Country = r.Competition.Country,
                CompetitionName = r.Competition.Name,
                IsMasters = r.Competition.IsMasters,
                IsAward = r.Competition.IsAward,
                AgeGroup = r.AgeGroup,
                CompetitionDate = r.CompetitionDate,
                StyleName = r.Style.Name,
                Distance = r.Distance,
                Gender = r.Gender,
                EventStyleAge = r.EventStyleAge,
                PoolType = r.Competition.PoolType,
                Position = r.Position,
                Heat = r.Heat,
                Lane = r.Lane,
                LastName = r.Swimmer.LastName,
                FirstName = r.Swimmer.FirstName,
                LastNameEn = r.Swimmer.LastNameEn,
                FirstNameEn = r.Swimmer.FirstNameEn,
                BirthYear = r.Swimmer.BirthYear,
                ClubName = r.Club.Name,
                ClubNameEn = r.Club.NameEn,
                TimeMillisecond = r.TimeMillisecond,
                TimeOriginal = r.TimeOriginal,
                TimeFail = r.TimeFail,
                TimeFailNote = r.TimeFailNote,
                InternationalPoints = r.InternationalPoints,
                Note = r.Note,
                IsRelay = r.RelayId != null,
                RelayTeamName = r.Relay != null ? r.Relay.TeamName : null,
                RelaySwimmersName = r.Relay != null ? r.Relay.SwimmersName : null
            })
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
            .Select(r => new ResultDto
            {
                Id = r.Id,
                Country = r.Competition.Country,
                CompetitionName = r.Competition.Name,
                IsMasters = r.Competition.IsMasters,
                IsAward = r.Competition.IsAward,
                AgeGroup = r.AgeGroup,
                CompetitionDate = r.CompetitionDate,
                StyleName = r.Style.Name,
                Distance = r.Distance,
                Gender = r.Gender,
                EventStyleAge = r.EventStyleAge,
                PoolType = r.Competition.PoolType,
                Position = r.Position,
                Heat = r.Heat,
                Lane = r.Lane,
                LastName = r.Swimmer.LastName,
                FirstName = r.Swimmer.FirstName,
                LastNameEn = r.Swimmer.LastNameEn,
                FirstNameEn = r.Swimmer.FirstNameEn,
                BirthYear = r.Swimmer.BirthYear,
                ClubName = r.Club.Name,
                ClubNameEn = r.Club.NameEn,
                TimeMillisecond = r.TimeMillisecond,
                TimeOriginal = r.TimeOriginal,
                TimeFail = r.TimeFail,
                TimeFailNote = r.TimeFailNote,
                InternationalPoints = r.InternationalPoints,
                Note = r.Note,
                IsRelay = r.RelayId != null,
                RelayTeamName = r.Relay != null ? r.Relay.TeamName : null,
                RelaySwimmersName = r.Relay != null ? r.Relay.SwimmersName : null
            })
            .FirstOrDefaultAsync();

        if (r == null) return NotFound();
        return Ok(r);
    }
}
