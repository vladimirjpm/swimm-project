using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Competitions;

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

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    public PagedResult<CompetitionRowDto> Result { get; private set; } =
        new([], 0, 1, PageSize);

    public IReadOnlyList<CategoryTagDto> Categories { get; private set; } = [];

    public IReadOnlyList<int> Years { get; private set; } = [];

    public async Task OnGetAsync()
    {
        if (PageNumber < 1) PageNumber = 1;
        Result = await _repo.GetPagedAsync(Search, CategoryKey, Year, PageNumber, PageSize);
        Categories = await _repo.GetAllCategoriesAsync();
        Years = await _repo.GetAvailableYearsAsync();
    }
}
