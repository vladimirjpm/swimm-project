using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.CompetitionsHub;

/// <summary>
/// Объединённая страница «соревнования»: Competitions (справочник БД) + Discovery (входящие
/// isr.org.il) одним списком со стадией жизненного цикла. Заменит обе старые страницы после
/// проверки (см. память admin-competitions-discovery-merge-plan). v1 — только чтение + переходы.
/// </summary>
[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private const int PageSize = 20;
    private readonly ICompetitionAdminRepository _repo;

    public IndexModel(ICompetitionAdminRepository repo) => _repo = repo;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "cat")]
    public string? CategoryKey { get; set; }

    [BindProperty(SupportsGet = true, Name = "year")]
    public int? Year { get; set; }

    /// <summary>Фильтр по стадии: OnSite | Imported | DbOnly | Ignored (пусто — все).</summary>
    [BindProperty(SupportsGet = true, Name = "stage")]
    public string? Stage { get; set; }

    /// <summary>Показывать тестовую синтетику (SYNTH Meet…). По умолчанию скрыта.</summary>
    [BindProperty(SupportsGet = true, Name = "synth")]
    public bool ShowSynthetic { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    public PagedResult<UnifiedCompetitionRowDto> Result { get; private set; } =
        new([], 0, 1, PageSize);

    public IReadOnlyList<CategoryTagDto> Categories { get; private set; } = [];

    public IReadOnlyList<int> Years { get; private set; } = [];

    public async Task OnGetAsync()
    {
        if (PageNumber < 1) PageNumber = 1;
        Result = await _repo.GetUnifiedAsync(Search, CategoryKey, Year, Stage, ShowSynthetic, PageNumber, PageSize);
        Categories = await _repo.GetAllCategoriesAsync();
        Years = await _repo.GetAvailableYearsAsync();
    }
}
