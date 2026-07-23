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
    private readonly IDataQualityService _quality;

    public IndexModel(IHubGroupAdminService service, IDataQualityService quality)
    {
        _service = service;
        _quality = quality;
    }

    public IReadOnlyList<HubGroupAdminRowDto> Groups { get; private set; } = [];

    /// <summary>Deep-link фильтр с дашборда: filter=official — только официальные группы.</summary>
    [BindProperty(SupportsGet = true, Name = "filter")]
    public string? Filter { get; set; }

    /// <summary>Deep-link с дашборда: tab=requests — показать секцию заявок на вступление (T3b).</summary>
    [BindProperty(SupportsGet = true, Name = "tab")]
    public string? Tab { get; set; }

    public CappedListDto<HubGroupJoinRequestRowDto> PendingRequests { get; private set; } = new(0, []);

    public async Task OnGetAsync()
    {
        var all = await _service.GetAllAsync();
        Groups = Filter == "official" ? all.Where(g => g.IsOfficial).ToList() : all;

        if (Tab == "requests")
            PendingRequests = await _quality.GetPendingJoinRequestsAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var result = await _service.DeleteAsync(id);
        TempData["Flash"] = result.Success ? "Группа удалена" : result.Error;
        return RedirectToPage("Index");
    }
}
