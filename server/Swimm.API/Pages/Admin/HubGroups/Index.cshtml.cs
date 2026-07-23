using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.HubGroups;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly IHubGroupAdminService _service;

    public IndexModel(IHubGroupAdminService service) => _service = service;

    public IReadOnlyList<HubGroupAdminRowDto> Groups { get; private set; } = [];

    /// <summary>Deep-link фильтр с дашборда: filter=official — только официальные группы.</summary>
    [BindProperty(SupportsGet = true, Name = "filter")]
    public string? Filter { get; set; }

    public async Task OnGetAsync()
    {
        var all = await _service.GetAllAsync();
        Groups = Filter == "official" ? all.Where(g => g.IsOfficial).ToList() : all;
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var result = await _service.DeleteAsync(id);
        TempData["Flash"] = result.Success ? "Группа удалена" : result.Error;
        return RedirectToPage("Index");
    }
}
