using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Swimm.API.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ApiModel : PageModel
{
    public void OnGet() { }
}
