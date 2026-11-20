using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Swimm.API.Pages.Admin;

/// <summary>
/// /Admin/Jobs — мониторинг фоновых служб (docs/admin-pages/jobs.md). Страница — только каркас:
/// виды «Таймлайн» и «Стена», панели и панель деталей рисуют скрипты <c>wwwroot/js/admin/jobs/</c>
/// из <c>GET /api/admin/jobs/monitor</c> (<c>JobsAdminController</c> → <c>IBackgroundJobsStatus</c>).
/// </summary>
[Authorize(Roles = "Admin")]
public class JobsModel : PageModel
{
    public void OnGet()
    {
    }
}
