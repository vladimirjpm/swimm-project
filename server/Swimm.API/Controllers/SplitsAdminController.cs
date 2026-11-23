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
    private readonly ISplitAutoAttachService _recorder;
    private readonly ILogger<SplitsAdminController> _logger;

    public SplitsAdminController(ISplitAttachService attach, ISplitAutoAttachService recorder,
        ILogger<SplitsAdminController> logger)
    {
        _attach = attach;
        _recorder = recorder;
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

        if (apply)
        {
            // Боевой прогон: итог — в аудит ВСЕГДА и каждому дню события (и «нечего», и сбой),
            // иначе шаг 9 у остальных дней застрял бы на прошлом итоге (например «не удалось»
            // автозапуска). Сбой источника — обычный исход, а не 500: причину читают в панели.
            var run = await _recorder.AttachByButtonAsync(id, loglig, ct);
            return Ok(Payload(run.Result, apply, ok: !run.Threw));
        }

        SplitAttachResult outcome;
        try
        {
            outcome = await _attach.AttachAsync(id, loglig, apply, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Доклейка промежуточных: сухой прогон сорвался (competitionId={Id})", id);
            return Ok(Payload(SplitAttachResult.Failed($"Не удалось: {ex.Message}"), apply, ok: false));
        }

        // Сухой прогон ничего не меняет — в аудит не идёт.
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
