using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Swimm.API.Pages.Admin.Discovery;

/// <summary>«Входящие» автозабора isr.org.il (фаза 6). Данные — через /api/admin/discovery.</summary>
[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
