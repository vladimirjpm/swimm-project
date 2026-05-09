using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Swimm.API.Pages.Admin;

[Authorize(Roles = "Admin")]
public class SettingsModel : PageModel
{
    public void OnGet() { }
}
