using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Swimm.API.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ImportHistoryModel : PageModel
{
    public void OnGet() { }
}
