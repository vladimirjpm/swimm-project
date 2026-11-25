using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;
using Swimm.Application.Mapping;

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
public class ProgrammeCheckAdminController(IProgrammeCheckService service, ISiblingDayPullService siblings) : ControllerBase
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
            acceptWarning = v.AcceptWarning,
            planFingerprint = v.PlanFingerprint,
            // Р124: «Затянуть этот день» — только у единственного кандидата.
            pullCandidate = ProgrammeCheckRules.PullCandidate(v),
            pullLabel = v.PullLabel,
            pullNote = v.PullNote,
            // Наружу — только int-номер loglig; адрес страницы собирает клиент из своей константы.
            candidates = (v.Candidates ?? []).Select(c => new
            {
                logligId = c.LogligId,
                title = c.Title,
                dates = c.Dates.Select(d => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)).ToList(),
            }),
        });
    }

    /// <summary>Тело «Затянуть этот день»: только int-номер соседа loglig (URL собирает провайдер).</summary>
    public sealed record SiblingPreviewRequest(int LogligId);

    /// <summary>
    /// Р124: превью дня-соседа — тот же ответ, что у «Затянуть» (шаг 6, одна разметка), плюс
    /// <c>sibling</c>: лежащий день, событие и его имя, конфликт бассейна. Отказ — 409 с причиной.
    /// </summary>
    [HttpPost("{discoveryId:int}/sibling-preview")]
    public async Task<IActionResult> SiblingPreview(int discoveryId, [FromBody] SiblingPreviewRequest request, CancellationToken ct)
    {
        var r = await siblings.PreviewAsync(discoveryId, request.LogligId, ct);
        if (r.Refusal != null || r.Preview is not { } p || r.Context is not { } c)
            return Conflict(new { error = MissingDayTexts.Refused(r.Refusal ?? "причина неизвестна") });
        return Ok(new { preview = DiscoveryAdminController.PreviewResponse(p), sibling = c });
    }

    /// <summary>
    /// Р124: «Импортировать» дня-соседа. Сервер перепроверяет всё сам (кандидат, дата, дня нет в базе,
    /// пересечение заплывов, параллельный импорт) и ставит импорт в очередь. Отказ — 409.
    /// </summary>
    [HttpPost("{discoveryId:int}/sibling-import")]
    public async Task<IActionResult> SiblingImport(int discoveryId, [FromBody] SiblingDayImportRequest request, CancellationToken ct)
    {
        var actor = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? User.Identity?.Name ?? "admin";
        var r = await siblings.ImportAsync(discoveryId, request, actor, ct);
        if (r.Refusal != null || r.JobId is not Guid jobId)
            return Conflict(new { error = MissingDayTexts.Refused(r.Refusal ?? "причина неизвестна") });
        return Accepted(new { jobId, eventId = r.EventId, newEventName = r.EventName });
    }

    /// <summary>Тело «Разделить»: отпечаток плана, который видел админ (из ответа check).</summary>
    public sealed record SplitRequest(string? PlanFingerprint);

    /// <summary>
    /// Запись и аудит — в сервисе (аудит пишется всегда, когда деление применено, даже если
    /// пересчёт после commit упал). Отказ — 409 с причиной.
    /// </summary>
    [HttpPost("{discoveryId:int}/split")]
    public async Task<IActionResult> Split(int discoveryId, [FromBody] SplitRequest? request, CancellationToken ct)
    {
        var outcome = await service.SplitAsync(discoveryId, request?.PlanFingerprint, ct);
        if (outcome.Refusal != null || outcome.Report is not { } r)
            return Conflict(new { error = $"Не разделено: {outcome.Refusal ?? "деление не применено"}" });

        var days = string.Join("; ", r.Days.Select(d =>
            $"день {d.DayNumber} {d.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} #{d.CompetitionId} — {d.Rows} строк"));
        return Ok(new
        {
            eventId = r.EventId,
            recalcError = outcome.RecalcError,
            message = outcome.RecalcError == null
                ? $"Разделено: событие #{r.EventId}; {days}. Дни пересчитаны."
                : $"Разделено: событие #{r.EventId}; {days}. ПЕРЕСЧЁТ НЕ ЗАВЕРШЁН ({outcome.RecalcError}) — пересчитайте дни.",
        });
    }
}
