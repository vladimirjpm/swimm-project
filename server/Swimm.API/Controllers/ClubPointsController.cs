using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

[ApiController]
[Route("api/club-points")]
public class ClubPointsController : ControllerBase
{
    private readonly IClubPointsRepository _repo;

    public ClubPointsController(IClubPointsRepository repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// Возвращает все правила начисления клубных очков со шкалой мест.
    /// Структура ответа зеркалит client/public/data/config/club-points-config.json.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetRules()
    {
        var rules = await _repo.GetRulesAsync();
        return Ok(new { rules });
    }
}
