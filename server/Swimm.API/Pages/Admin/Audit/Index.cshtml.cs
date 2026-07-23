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

    // Имя параметра — «p», а НЕ «page» («page» зарезервировано роутингом Razor Pages).
    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public PagedResult<AdminAuditRowDto> Result { get; private set; } = new([], 0, 1, PageSize);

    public IReadOnlyList<string> Actions { get; private set; } = [];

    public async Task OnGetAsync()
    {
        if (PageNumber < 1) PageNumber = 1;
        Result = await _repo.QueryAsync(new AdminAuditFilter(
            Action: string.IsNullOrWhiteSpace(Action) ? null : Action,
            Search: Search,
            Page: PageNumber,
            PageSize: PageSize));
        Actions = await _repo.GetDistinctActionsAsync();
    }

    public string PageUrl(int page) => Url.Page("Index", new { q = Search, action = Action, p = page })!;
}
