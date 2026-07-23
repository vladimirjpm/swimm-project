using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Media;

/// <summary>Битые ссылки UserMedia (фаза 7.5). Проверка живости — по кнопке (POST /api/admin/media/check-links).</summary>
[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly IUserMediaLinkChecker _checker;

    public IndexModel(IUserMediaLinkChecker checker) => _checker = checker;

    public IReadOnlyList<BrokenMediaRowDto> Broken { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Broken = await _checker.GetBrokenAsync();
    }
}
