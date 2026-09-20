using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// Доклейка промежуточных к строкам, которые УЖЕ в базе (Д5 плана
/// docs/plans/splits-attach-without-repull-plan.md) — тот же <see cref="ISplitAttachService"/>,
/// что и у CLI-флага <c>--attach-splits</c>: правило живёт в одном месте, кнопка и консоль
/// не могут разъехаться.
///
/// Операция пишет ровно два поля (<c>RelayMembers.SplitTime</c>, <c>Results.TimeSplit</c>),
/// строк не создаёт и не удаляет; сухой прогон — <c>apply=false</c>.
/// </summary>
[ApiController]
[Route("api/admin/splits")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class SplitsAdminController : ControllerBase
{
    private readonly ISplitAttachService _attach;
    private readonly IAdminAuditService _audit;
    private readonly ILogger<SplitsAdminController> _logger;

    public SplitsAdminController(ISplitAttachService attach, IAdminAuditService audit,
        ILogger<SplitsAdminController> logger)
    {
        _attach = attach;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// Доклеить промежуточные соревнованию. Прогон идёт по ВСЕМ дням события, какой бы день
    /// ни передали (§6-2 плана), поэтому и в отчёте — разбивка по дням.
    /// </summary>
    /// <param name="id">Любой день события.</param>
    /// <param name="apply">false (дефолт) — сухой прогон: отчёт тот же, в базу ничего не пишем.</param>
    /// <param name="loglig">Нужен только стартам без строки discovery (склейки, старый JSON).</param>
    [HttpPost("{id:int}")]
    public async Task<IActionResult> Attach(int id, [FromQuery] bool apply = false,
        [FromQuery] int? loglig = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Доклейка промежуточных: старт (competitionId={Id}, apply={Apply}, loglig={Loglig})",
            id, apply, loglig);

        SplitAttachResult outcome;
        try
        {
            outcome = await _attach.AttachAsync(id, loglig, apply, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            // Источник недоступен или не разобрался — это обычный исход, а не 500: админу
            // нужно прочитать причину в панели, а не в логах.
            _logger.LogWarning(ex, "Доклейка промежуточных: сорвалась (competitionId={Id})", id);
            return Ok(Payload(SplitAttachResult.Failed($"Не удалось: {ex.Message}"), apply, ok: false));
        }

        // Пишем в аудит только боевой прогон — сухой ничего не меняет.
        if (apply && (outcome.Report.LegWrites > 0 || outcome.Report.SwimWrites > 0))
            await _audit.LogAsync("competition.attach-splits", "Competition", id.ToString(),
                $"Доклеены промежуточные (#{id}, loglig {outcome.LogligId}): ног {outcome.Report.LegWrites}, "
                + $"личных строк {outcome.Report.SwimWrites}",
                outcome.Report, ct);

        return Ok(Payload(outcome, apply, ok: true));
    }

    /// <summary>
    /// Плоский ответ для панели: строка отчёта (та же, что печатает CLI) + числа для решения
    /// «есть что писать» и разбивка по дням.
    /// </summary>
    private static object Payload(SplitAttachResult r, bool apply, bool ok) => new
    {
        ok,
        applied = r.Applied,
        logligId = r.LogligId,
        // Message уже содержит строку отчёта (SplitAttachReport.ToString) — второй копии в
        // панели быть не должно.
        message = r.Message,
        legWrites = r.Report.LegWrites,
        swimWrites = r.Report.SwimWrites,
        dryRun = !apply,
        days = r.Days.Select(d => new { d.CompetitionId, d.Name, d.Date, d.LegWrites, d.SwimWrites })
    };
}
