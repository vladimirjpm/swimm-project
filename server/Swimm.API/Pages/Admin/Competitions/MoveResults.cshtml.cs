using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Competitions;

/// <summary>
/// Массовый перенос результатов между соревнованиями (фаза 7.3) — для склейки дублей.
/// Двухшаговый: dry-run превью (GET с обоими Id) → применение (POST). Пишет аудит.
/// </summary>
[Authorize(Roles = "Admin")]
public class MoveResultsModel : PageModel
{
    private readonly IResultTransferService _transfer;
    private readonly IAdminAuditService _audit;

    public MoveResultsModel(IResultTransferService transfer, IAdminAuditService audit)
    {
        _transfer = transfer;
        _audit = audit;
    }

    [BindProperty(SupportsGet = true, Name = "source")]
    public int? SourceId { get; set; }

    [BindProperty(SupportsGet = true, Name = "target")]
    public int? TargetId { get; set; }

    public ResultTransferReport? Preview { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        if (SourceId is > 0 && TargetId is > 0)
            await LoadPreviewAsync();
    }

    public async Task<IActionResult> OnPostApplyAsync()
    {
        if (SourceId is not > 0 || TargetId is not > 0)
        {
            Error = "Укажите Id источника и цели.";
            return Page();
        }

        try
        {
            var report = await _transfer.MoveResultsAsync(SourceId.Value, TargetId.Value, apply: true);
            await _audit.LogAsync("competition.transfer-results", "Competition", TargetId.Value.ToString(),
                $"Перенос {report.ResultsToMove} результатов: #{report.SourceId} «{report.SourceName}» → #{report.TargetId} «{report.TargetName}»",
                report);
            TempData["Flash"] = $"Перенесено результатов: {report.ResultsToMove}. Источник #{report.SourceId} теперь пуст.";
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
            return Page();
        }

        // Показываем новое состояние (источник теперь пуст).
        return RedirectToPage("MoveResults", new { source = SourceId, target = TargetId });
    }

    private async Task LoadPreviewAsync()
    {
        try
        {
            Preview = await _transfer.MoveResultsAsync(SourceId!.Value, TargetId!.Value, apply: false);
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
        }
    }
}
