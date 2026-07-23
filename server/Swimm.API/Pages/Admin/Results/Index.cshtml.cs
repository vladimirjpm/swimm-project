using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Swimm.API.Pages.Admin.Results;

/// <summary>
/// Точка входа ручной правки результата (фаза 7.2 B). Переход к форме по Id результата
/// (Id виден в публичной выдаче / браузере данных Admin/Api). Полноценный поиск — задел на 7.3.
/// </summary>
[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    [BindProperty]
    public long ResultId { get; set; }

    public void OnGet() { }

    public IActionResult OnPost()
    {
        if (ResultId <= 0)
        {
            TempData["Flash"] = "Укажите положительный Id результата";
            return RedirectToPage("Index");
        }
        return RedirectToPage("Edit", new { id = ResultId });
    }
}
