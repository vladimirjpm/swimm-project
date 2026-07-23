using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Media;

/// <summary>
/// Здоровье ссылок UserMedia (фаза 7.5) + deep-link выборки «здоровье данных» (T3b):
/// три вкладки — broken-links (по умолчанию, проверка живости по кнопке), unchecked
/// (ещё не проверенные) и moderation-pending (read-only обзор заявок на публикацию).
/// </summary>
[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly IUserMediaLinkChecker _checker;
    private readonly IDataQualityService _quality;

    public IndexModel(IUserMediaLinkChecker checker, IDataQualityService quality)
    {
        _checker = checker;
        _quality = quality;
    }

    /// <summary>broken-links (по умолчанию) | unchecked | moderation-pending.</summary>
    [BindProperty(SupportsGet = true, Name = "filter")]
    public string View { get; set; } = "broken-links";

    public IReadOnlyList<BrokenMediaRowDto> Broken { get; private set; } = [];

    public CappedListDto<BrokenMediaRowDto> Unchecked { get; private set; } = new(0, []);

    public CappedListDto<ModerationPendingRowDto> ModerationPending { get; private set; } = new(0, []);

    public async Task OnGetAsync()
    {
        if (View is not ("unchecked" or "moderation-pending"))
            View = "broken-links";

        switch (View)
        {
            case "unchecked":
                Unchecked = await _checker.GetUncheckedAsync();
                break;
            case "moderation-pending":
                ModerationPending = await _quality.GetModerationPendingAsync();
                break;
            default:
                Broken = await _checker.GetBrokenAsync();
                break;
        }
    }
}
