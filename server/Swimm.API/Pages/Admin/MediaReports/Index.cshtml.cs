using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.API.Pages.Admin.Shared;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.MediaReports;

/// <summary>
/// Жалобы «Report» на медиа (Р62, docs/data-integrity.md; страница — docs/admin-pages/mediareports.md).
/// Решает только админ сайта: «оставить» (жалобы закрываются, медиа снова видно) или «снять»
/// (публикации отклоняются, заново подать нельзя). Кто пожаловался — видно только здесь.
/// </summary>
[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly IMediaReportService _reports;
    private readonly ISettingsService _settings;

    public IndexModel(IMediaReportService reports, ISettingsService settings, IConfiguration config, IWebHostEnvironment env)
    {
        _reports = reports;
        _settings = settings;
        PublicSiteBaseUrl = PublicSite.BaseUrl(config, env);
    }

    /// <summary>open (по умолчанию) — ждут решения; closed — разобранные.</summary>
    [BindProperty(SupportsGet = true, Name = "status")]
    public string? Status { get; set; }

    public bool ShowOpen => Status != "closed";

    public List<MediaReportQueueItemDto> Items { get; private set; } = [];
    public int OpenCount { get; private set; }
    public int Threshold { get; private set; }
    public string PublicSiteBaseUrl { get; }

    public async Task OnGetAsync()
    {
        Items = await _reports.GetQueueAsync(ShowOpen);
        OpenCount = ShowOpen ? Items.Count : await _reports.CountOpenAsync();
        Threshold = Swimm.Application.Mapping.MediaReportRules.HideThreshold(_settings);
    }

    public async Task<IActionResult> OnPostKeepAsync(int mediaId) => await DecideAsync(mediaId, keep: true);

    public async Task<IActionResult> OnPostRemoveAsync(int mediaId) => await DecideAsync(mediaId, keep: false);

    private async Task<IActionResult> DecideAsync(int mediaId, bool keep)
    {
        var ok = await _reports.DecideAsync(mediaId, keep, CurrentUserId());
        TempData["Flash"] = !ok ? $"Медиа #{mediaId} не найдено"
            : keep ? $"Медиа #{mediaId} оставлено — снова видно" : $"Медиа #{mediaId} снято";
        return RedirectToPage("Index", new { status = Status });
    }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
}
