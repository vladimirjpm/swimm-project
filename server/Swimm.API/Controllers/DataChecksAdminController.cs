using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// Реестр проверок данных (docs/data-integrity.md, фаза Д3) — API страницы /Admin/Health.
/// Прогон читающий: проверки ставят диагноз, лечение остаётся отдельным осознанным действием.
/// </summary>
[ApiController]
[Route("api/admin/data-checks")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class DataChecksAdminController(IDataCheckRunner runner, IAdminAuditService audit) : ControllerBase
{
    /// <summary>Текущие находки по проверкам + история прогонов.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(new
        {
            groups = await runner.GetCurrentAsync(ct),
            history = await runner.GetHistoryAsync(10, ct)
        });

    /// <summary>Прогнать все проверки.</summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        var run = await runner.RunAllAsync("manual", ct);
        return Ok(new
        {
            run,
            groups = await runner.GetCurrentAsync(ct),
            history = await runner.GetHistoryAsync(10, ct)
        });
    }

    public sealed record AcceptRequest(string? Note);

    /// <summary>«Принято как есть» — находка неустранима, но решение должно пережить прогоны.</summary>
    [HttpPost("{id:int}/accept")]
    public async Task<IActionResult> Accept(int id, [FromBody] AcceptRequest? request, CancellationToken ct)
    {
        if (!await runner.AcceptAsync(id, request?.Note, ct))
            return NotFound(new { error = "Находка не найдена или уже закрыта" });

        await audit.LogAsync("datacheck.accept", "DataCheckFinding", id.ToString(),
            $"Находка #{id} принята как есть" + (string.IsNullOrWhiteSpace(request?.Note) ? "" : $": {request!.Note}"), ct: ct);
        return Ok();
    }

    public sealed record FixClubRuleRequest(int RuleId);

    public sealed record FixGenderRequest(string Gender);

    /// <summary>
    /// Проставить пол пловцу прямо из списка находок (`results.no-gender`). Правит и строки
    /// результатов без пола: проверка смотрит именно на них, а Results.Gender заполняется на
    /// импорте — иначе находка висела бы до переимпорта и кнопка выглядела бы сломанной.
    /// </summary>
    [HttpPost("{id:int}/fix-gender")]
    public async Task<IActionResult> FixGender(int id, [FromBody] FixGenderRequest request, CancellationToken ct)
    {
        var rows = await runner.FixSwimmerGenderAsync(id, request.Gender, ct);
        if (rows is null)
            return BadRequest(new { error = "Находка не найдена, не поддерживает это исправление или пол задан неверно" });

        await audit.LogAsync("datacheck.fix-gender", "DataCheckFinding", id.ToString(),
            $"Находка #{id}: пол '{request.Gender}' проставлен пловцу; строк результата поправлено — {rows}",
            new { findingId = id, request.Gender, rows }, ct);

        return Ok(new { rows });
    }

    /// <summary>
    /// Выровнять пол пловца из находки `results.gender-vs-card`: пол уходит в карточку И во
    /// ВСЕ его личные строки. Отдельный эндпоинт, а не флаг у fix-gender: перезапись
    /// напечатанного в протоколе пола — другое по смыслу действие, и в аудите оно должно
    /// выглядеть иначе.
    /// </summary>
    [HttpPost("{id:int}/fix-gender-align")]
    public async Task<IActionResult> FixGenderAlign(int id, [FromBody] FixGenderRequest request, CancellationToken ct)
    {
        var rows = await runner.AlignSwimmerGenderAsync(id, request.Gender, ct);
        if (rows is null)
            return BadRequest(new { error = "Находка не найдена, не поддерживает это исправление или пол задан неверно" });

        await audit.LogAsync("datacheck.fix-gender-align", "DataCheckFinding", id.ToString(),
            $"Находка #{id}: пол '{request.Gender}' выровнен по карточке и строкам; строк переписано — {rows}",
            new { findingId = id, request.Gender, rows }, ct);

        return Ok(new { rows });
    }

    /// <summary>Привязать правило клубных очков прямо из находки (без захода в карточку).</summary>
    [HttpPost("{id:int}/fix-club-rule")]
    public async Task<IActionResult> FixClubRule(int id, [FromBody] FixClubRuleRequest request, CancellationToken ct)
    {
        if (!await runner.FixCompetitionClubRuleAsync(id, request.RuleId, ct))
            return BadRequest(new { error = "Находка не найдена, не поддерживает это исправление или правило неизвестно" });

        await audit.LogAsync("datacheck.fix-club-rule", "DataCheckFinding", id.ToString(),
            $"Находка #{id}: соревнованию привязано правило клубных очков #{request.RuleId}, зачёт пересчитан",
            new { findingId = id, request.RuleId }, ct);

        return Ok(new { ok = true });
    }

    /// <summary>
    /// Массово дописать пол в строки, где он известен у пловца. Не гадает: находки, где пол
    /// неизвестен и у пловца, остаются человеку — их и надо разбирать руками.
    /// </summary>
    [HttpPost("fix-gender-all")]
    public async Task<IActionResult> FixGenderAll(CancellationToken ct)
    {
        var (findings, rows) = await runner.FixAllKnownSwimmerGendersAsync(ct);

        await audit.LogAsync("datacheck.fix-gender-all", "DataCheckFinding", null,
            $"Массово проставлен пол по пловцам: пловцов — {findings}, строк результата — {rows}",
            new { findings, rows }, ct);

        return Ok(new { findings, rows });
    }

    /// <summary>Вернуть принятую находку в работу.</summary>
    [HttpPost("{id:int}/reopen")]
    public async Task<IActionResult> Reopen(int id, CancellationToken ct)
    {
        if (!await runner.ReopenAsync(id, ct))
            return NotFound(new { error = "Находка не найдена или не была принята" });

        await audit.LogAsync("datacheck.reopen", "DataCheckFinding", id.ToString(),
            $"Находка #{id} возвращена в работу", ct: ct);
        return Ok();
    }
}
