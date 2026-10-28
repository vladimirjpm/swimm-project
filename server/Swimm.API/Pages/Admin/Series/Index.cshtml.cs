using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Series;

/// <summary>
/// Серии турниров (docs/admin-pages/series.md): повторяющиеся из года в год соревнования, к
/// которым привязан регламент официального награждения. Список + заведение новой серии; всё
/// остальное — на странице Edit.
/// </summary>
[Authorize(Roles = "Admin")]
public class IndexModel(ISeriesAdminRepository repo, IAdminAuditService audit) : PageModel
{
    public IReadOnlyList<SeriesRowDto> Series { get; private set; } = [];

    public async Task OnGetAsync() => Series = await repo.GetAllAsync();

    public async Task<IActionResult> OnPostCreateAsync(string name, string? nameEn)
    {
        var result = await repo.CreateSeriesAsync(new SeriesInputDto(name ?? "", nameEn, null));
        if (!result.Success)
        {
            TempData["Error"] = result.Error;
            return RedirectToPage("Index");
        }

        await audit.LogAsync("series.create", "CompetitionSeries", result.Id.ToString(), $"Создана серия «{name?.Trim()}»");
        TempData["Flash"] = "Серия создана";
        return RedirectToPage("Edit", new { id = result.Id });
    }
}
