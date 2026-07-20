using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Swimm.API.Pages.Admin.Swimmers;

/// <summary>Привязка Loglig ID к пловцам (docs/loglig-id-plan.md, шаг 5). Данные — через /api/admin/loglig.</summary>
[Authorize(Roles = "Admin")]
public class LogligModel(IConfiguration configuration) : PageModel
{
    /// <summary>Без валидного seasonId карточка loglig отдаёт 500 — ссылки строим с ним.
    /// Тот же конфиг, что у LogligClient (Loglig:SeasonId, обновлять раз в сезон).</summary>
    public int SeasonId { get; private set; }

    public void OnGet()
    {
        SeasonId = configuration.GetValue("Loglig:SeasonId", 1715);
    }
}
