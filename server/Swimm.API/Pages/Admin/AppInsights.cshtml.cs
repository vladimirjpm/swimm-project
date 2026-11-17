using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Swimm.API.Pages.Admin;

/// <summary>
/// /Admin/AppInsights — телеметрия прод-сайта из Azure Application Insights (docs/admin-pages/appinsights.md).
/// Страница открывается сразу с каркасом: в Azure сервер из <c>OnGet</c> НЕ ходит, цифры доезжают
/// скриптом (wwwroot/js/admin/appinsights.js) запросом GET /api/admin/appinsights/overview.
/// </summary>
[Authorize(Roles = "Admin")]
public class AppInsightsModel : PageModel
{
    public void OnGet() { }
}
