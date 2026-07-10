using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.HubGroupClubRequests;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly IHubGroupClubRequestAdminService _service;

    public IndexModel(IHubGroupClubRequestAdminService service) => _service = service;

    public IReadOnlyList<HubGroupClubRequestAdminRowDto> Requests { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Requests = await _service.GetAllAsync();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        var result = await _service.ApproveAsync(id, CurrentUserId());
        TempData["Flash"] = result.Success ? "Заявка одобрена" : result.Error;
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostRejectAsync(int id)
    {
        var result = await _service.RejectAsync(id, CurrentUserId());
        TempData["Flash"] = result.Success ? "Заявка отклонена" : result.Error;
        return RedirectToPage("Index");
    }

    private int CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
}
