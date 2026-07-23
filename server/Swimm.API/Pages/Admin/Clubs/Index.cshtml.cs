using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Swimm.API.Pages.Admin.Clubs;

/// <summary>Склейка клубов-дублей (фаза C). Данные — через /api/admin/clubs.</summary>
[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
