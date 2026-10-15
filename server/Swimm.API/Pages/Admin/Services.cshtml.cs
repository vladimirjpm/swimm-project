using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.API.Pages.Admin.Shared;

namespace Swimm.API.Pages.Admin;

/// <summary>
/// /Admin/Services — все внешние сайты и кабинеты проекта: зачем каждый и куда нажимать
/// (docs/admin-pages/services.md). Содержимое — <see cref="ServicesCatalog"/>.
/// </summary>
[Authorize(Roles = "Admin")]
public class ServicesModel : PageModel
{
    public IReadOnlyList<ServiceGroup> Groups => ServicesCatalog.Groups;

    public void OnGet() { }
}
