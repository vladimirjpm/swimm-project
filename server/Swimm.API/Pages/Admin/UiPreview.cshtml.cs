using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Swimm.API.Pages.Admin;

/// <summary>Витрина UI-компонентов админки (стайлгайд для фаз 2–5 редизайна).</summary>
[Authorize(Roles = "Admin")]
public class UiPreviewModel : PageModel
{
    public void OnGet() { }
}
