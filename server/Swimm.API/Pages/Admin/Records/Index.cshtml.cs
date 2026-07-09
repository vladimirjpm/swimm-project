using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;

namespace Swimm.API.Pages.Admin.Records;

[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private const int PageSize = 50;
    private readonly IRecordAdminRepository _repo;

    public IndexModel(IRecordAdminRepository repo) => _repo = repo;

    [BindProperty(SupportsGet = true, Name = "tab")]
    public string Tab { get; set; } = "records";

    [BindProperty(SupportsGet = true, Name = "regionType")]
    public string? RegionType { get; set; }

    [BindProperty(SupportsGet = true, Name = "regionCode")]
    public string? RegionCode { get; set; }

    [BindProperty(SupportsGet = true, Name = "category")]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true, Name = "kind")]
    public string? Kind { get; set; }

    [BindProperty(SupportsGet = true, Name = "gender")]
    public string? Gender { get; set; }

    [BindProperty(SupportsGet = true, Name = "poolType")]
    public string? PoolType { get; set; }

    [BindProperty(SupportsGet = true, Name = "style")]
    public string? Style { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    public PagedResult<RecordDto> Records { get; private set; } = new([], 0, 1, PageSize);
    public PagedResult<NormativeStandardDto> Standards { get; private set; } = new([], 0, 1, PageSize);

    public IReadOnlyList<string> RegionTypes { get; } = Record.RegionTypes.OrderBy(x => x).ToList();
    public IReadOnlyList<string> RecordCategories { get; } = Record.Categories.OrderBy(x => x).ToList();
    public IReadOnlyList<string> StandardKinds { get; } = NormativeStandard.Kinds.OrderBy(x => x).ToList();

    public async Task<IActionResult> OnGetAsync()
    {
        Tab = Tab == "standards" ? "standards" : "records";
        if (PageNumber < 1) PageNumber = 1;

        if (Tab == "standards")
            Standards = await _repo.GetStandardsAsync(new StandardFilter(Kind, Gender, PoolType, Style), PageNumber, PageSize);
        else
            Records = await _repo.GetRecordsAsync(new RecordFilter(RegionType, RegionCode, Category, Gender, PoolType, Style), PageNumber, PageSize);

        return Page();
    }
}
