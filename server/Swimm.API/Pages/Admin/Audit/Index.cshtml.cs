using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Audit;

/// <summary>
/// Журнал ручных мутаций админки (фаза 7.4) — read-only лента Sys_AdminAudit с фильтрами
/// по действию/поиску и пагинацией. Пишущей стороны здесь нет: строки создаёт
/// IAdminAuditService из самих мутаций.
/// </summary>
[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private const int PageSize = 30;
    private readonly IAdminAuditRepository _repo;

    public IndexModel(IAdminAuditRepository repo) => _repo = repo;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "action")]
    public string? Action { get; set; }

    /// <summary>Deep-link фильтр по периоду: 24h/7d/30d (иное/пусто — всё время).</summary>
    [BindProperty(SupportsGet = true, Name = "period")]
    public string? Period { get; set; }

    // Имя параметра — «p», а НЕ «page» («page» зарезервировано роутингом Razor Pages).
    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public PagedResult<AdminAuditRowDto> Result { get; private set; } = new([], 0, 1, PageSize);

    public IReadOnlyList<string> Actions { get; private set; } = [];

    public async Task OnGetAsync()
    {
        if (PageNumber < 1) PageNumber = 1;
        DateTime? sinceUtc = Period switch
        {
            "24h" => DateTime.UtcNow.AddHours(-24),
            "7d" => DateTime.UtcNow.AddDays(-7),
            "30d" => DateTime.UtcNow.AddDays(-30),
            _ => null
        };
        Result = await _repo.QueryAsync(new AdminAuditFilter(
            Action: string.IsNullOrWhiteSpace(Action) ? null : Action,
            Search: Search,
            Page: PageNumber,
            PageSize: PageSize,
            SinceUtc: sinceUtc));
        Actions = await _repo.GetDistinctActionsAsync();
    }

    public string PageUrl(int page) => Url.Page("Index", new { q = Search, action = Action, period = Period, p = page })!;
}
