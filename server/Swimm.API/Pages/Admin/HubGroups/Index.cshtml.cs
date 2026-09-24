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

    /// <summary>
    /// Фильтр списка: official — только официальные (deep-link с дашборда), test — только тестовые
    /// (HubGroup.IsTest), real — без тестовых; пусто — все.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "filter")]
    public string? Filter { get; set; }

    /// <summary>Deep-link с дашборда: tab=requests — показать секцию заявок на вступление (T3b).</summary>
    [BindProperty(SupportsGet = true, Name = "tab")]
    public string? Tab { get; set; }

    public CappedListDto<HubGroupJoinRequestRowDto> PendingRequests { get; private set; } = new(0, []);

    public async Task OnGetAsync()
    {
        var all = await _service.GetAllAsync();
        Groups = Filter switch
        {
            "official" => all.Where(g => g.IsOfficial).ToList(),
            "test" => all.Where(g => g.IsTest).ToList(),
            "real" => all.Where(g => !g.IsTest).ToList(),
            _ => all,
        };

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
