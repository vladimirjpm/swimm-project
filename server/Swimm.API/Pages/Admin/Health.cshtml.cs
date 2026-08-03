using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Swimm.API.Pages.Admin;

/// <summary>
/// Здоровье данных (docs/data-integrity.md, фаза Д3) — единое место всех проверок
/// целостности. Данные страница берёт через API (/api/admin/data-checks), чтобы прогон
/// не блокировал рендер: проверки ходят в БД десятками запросов.
/// </summary>
[Authorize(Roles = "Admin")]
public class HealthModel : PageModel
{
    public void OnGet() { }
}
