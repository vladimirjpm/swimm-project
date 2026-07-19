using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Swimm.API.Pages.Admin.Swimmers;

/// <summary>Привязка Loglig ID к пловцам (docs/loglig-id-plan.md, шаг 5). Данные — через /api/admin/loglig.</summary>
[Authorize(Roles = "Admin")]
public class LogligModel : PageModel
{
    public void OnGet()
    {
    }
}
