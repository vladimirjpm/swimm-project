using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Controllers;

/// <summary>
/// Персональный план на соревнование в табе Start list
/// (docs/plans/start-list-ticket-plan.md, шаг Т3): за кем следит пользователь и две галочки.
///
/// Только для залогиненного: у гостя ту же роль играет localStorage браузера, и разницу
/// прячет один клиентский шов <c>useStartListPlan</c>. Данные приватные — план говорит, где
/// будет ребёнок и придёт ли родитель, — поэтому и таблица <c>Sys_</c> без публичного гранта.
/// </summary>
[ApiController]
[Route("api/me/start-list-plans")]
[Authorize]
[AutoValidateAntiforgeryToken]
public class StartListPlansController : ControllerBase
{
    private readonly IStartListPlanRepository _plans;

    public StartListPlansController(IStartListPlanRepository plans)
    {
        _plans = plans;
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Все планы пользователя — свежие первыми.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        return Ok(await _plans.GetAllAsync(userId.Value, ct));
    }

    /// <summary>
    /// План на одно соревнование. <b>404 — плана нет</b>, и это осмысленный ответ, а не
    /// ошибка: витрина подставляет тогда избранных. Пустой сохранённый план — совсем другое
    /// состояние («я всё снял сам»), и он приходит с кодом 200 и пустыми списками.
    /// </summary>
    [HttpGet("{orgCompId:int}")]
    public async Task<IActionResult> Get(int orgCompId, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var plan = await _plans.GetAsync(userId.Value, orgCompId, ct);
        return plan is null ? NotFound() : Ok(plan);
    }

    /// <summary>Сохранить состав целиком (создать или переписать).</summary>
    [HttpPut("{orgCompId:int}")]
    public async Task<IActionResult> Save(
        int orgCompId, [FromBody] StartListPlanSaveRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        return Ok(await _plans.SaveAsync(userId.Value, orgCompId, request, ct));
    }

    /// <summary>Забыть план: пользователь возвращается к дефолту «мои избранные».</summary>
    [HttpDelete("{orgCompId:int}")]
    public async Task<IActionResult> Delete(int orgCompId, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        return await _plans.DeleteAsync(userId.Value, orgCompId, ct) ? NoContent() : NotFound();
    }
}
