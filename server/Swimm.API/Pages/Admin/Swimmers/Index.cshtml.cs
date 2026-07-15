using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Swimm.API.Pages.Admin.Swimmers;

/// <summary>Склейка пловцов-дублей (фаза 7.2). Данные — через /api/admin/swimmers.</summary>
[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
