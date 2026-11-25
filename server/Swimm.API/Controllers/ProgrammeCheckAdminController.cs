using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// Р123: кнопки блока «Что не так» карточки /Admin/Competitions у отметки Р115 («затянуто N из M
/// дней»). <c>check</c> — сверка программы loglig с днями в базе (сеть только по клику);
/// <c>split</c> — «Разделить по дням» (запись тем же сервисом, что CLI <c>--split-days --apply</c>).
/// Оба — POST под antiforgery (заголовок X-XSRF-TOKEN, как у остальных действий страницы).
/// </summary>
[ApiController]
[Route("api/admin/programme-check")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class ProgrammeCheckAdminController(IProgrammeCheckService service, IAdminAuditService audit) : ControllerBase
{
    [HttpPost("{discoveryId:int}/check")]
    public async Task<IActionResult> Check(int discoveryId, CancellationToken ct)
    {
        var v = await service.CheckAsync(discoveryId, ct);
        return Ok(new
        {
            kind = v.Kind.ToString(),
            headline = v.Headline,
            lines = v.Lines,
            hint = v.Hint,
            canSplit = v.CanSplit,
            splitCompetitionId = v.SplitCompetitionId,
            splitBlocked = v.SplitBlocked,
            acceptPrimary = v.AcceptPrimary,
        });
    }

    [HttpPost("{discoveryId:int}/split")]
    public async Task<IActionResult> Split(int discoveryId, CancellationToken ct)
    {
        var outcome = await service.SplitAsync(discoveryId, ct);
        if (outcome.Refusal != null || outcome.Report is not { } r)
            return Conflict(new { error = $"Не разделено: {outcome.Refusal ?? "деление не применено"}" });

        var days = string.Join("; ", r.Days.Select(d =>
            $"день {d.DayNumber} {d.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} #{d.CompetitionId} — {d.Rows} строк"));
        // Тот же вид записи, что у CLI --split-days --apply (MultidayCli), плюс откуда нажато.
        await audit.LogAsync("competition.split-days", "Competition", r.CompetitionId.ToString(CultureInfo.InvariantCulture),
            $"Многодневка разделена по дням (loglig {r.LogligId}): событие #{r.EventId}, {days} — кнопка карточки /Admin/Competitions (discovery #{discoveryId})");

        return Ok(new
        {
            eventId = r.EventId,
            message = $"Разделено: событие #{r.EventId}; {days}. Дни пересчитаны.",
        });
    }
}
