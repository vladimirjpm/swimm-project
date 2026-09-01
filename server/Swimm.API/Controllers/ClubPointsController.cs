using Microsoft.AspNetCore.Mvc;
using Swimm.API.Http;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

[ApiController]
[Route("api/club-points")]
public class ClubPointsController : ControllerBase
{
    private readonly IClubPointsRepository _repo;
    private readonly ICacheService _cache;

    private const string CacheControlValue = "public, max-age=300";
    private static readonly TimeSpan PayloadTtl = TimeSpan.FromHours(1);

    public ClubPointsController(IClubPointsRepository repo, ICacheService cache)
    {
        _repo = repo;
        _cache = cache;
    }

    /// <summary>
    /// Возвращает все правила начисления клубных очков со шкалой мест.
    /// Структура ответа зеркалит клиентский интерфейс PointsRule (исторически — статику
    /// client/public/data/config/club-points-config.json, удалена: правила идут только отсюда).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetRules()
    {
        return await this.CachedJson(_cache, "http:club-points",
            async () => new { rules = await _repo.GetRulesAsync() },
            PayloadTtl, CacheControlValue);
    }
}
