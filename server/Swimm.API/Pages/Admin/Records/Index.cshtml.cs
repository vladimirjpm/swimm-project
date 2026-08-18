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
    private readonly IRecordQualityService _quality;

    public IndexModel(IRecordAdminRepository repo, IRecordQualityService quality)
    {
        _repo = repo;
        _quality = quality;
    }

    [BindProperty(SupportsGet = true, Name = "tab")]
    public string Tab { get; set; } = "records";

    [BindProperty(SupportsGet = true, Name = "regionType")]
    public string? RegionType { get; set; }

    [BindProperty(SupportsGet = true, Name = "regionCode")]
    public string? RegionCode { get; set; }

    /// <summary>
    /// Deep-link алиас с дашборда: region=world|israel задаёт пару regionType/regionCode
    /// одним параметром, только если явные regionType/regionCode не заданы. Неизвестное
    /// значение игнорируется.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "region")]
    public string? RegionAlias { get; set; }

    /// <summary>Deep-link алиас с дашборда: filter=issues открывает вкладку «Спорные записи».</summary>
    [BindProperty(SupportsGet = true, Name = "filter")]
    public string? Filter { get; set; }

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

    /// <summary>Фильтр статуса на вкладке «Спорные записи»; пусто — все.</summary>
    [BindProperty(SupportsGet = true, Name = "status")]
    public string? IssueStatus { get; set; }

    public PagedResult<RecordDto> Records { get; private set; } = new([], 0, 1, PageSize);
    public PagedResult<NormativeStandardDto> Standards { get; private set; } = new([], 0, 1, PageSize);

    /// <summary>
    /// Реестр спорных записей справочника (docs/plans/records-quality-plan.md).
    /// ⚠ Ошибки источника не чиним — помечаем; вкладка про метки, а не про правку рекордов.
    /// </summary>
    public PagedResult<RecordIssueDto> Issues { get; private set; } = new([], 0, 1, PageSize);

    public IReadOnlyList<string> IssueStatuses { get; } = RecordIssueStatuses.All;
    public IReadOnlyList<string> IssueReasons { get; } = RecordIssueReasons.All;

    public IReadOnlyList<string> RegionTypes { get; } = Record.RegionTypes.OrderBy(x => x).ToList();
    public IReadOnlyList<string> RecordCategories { get; } = Record.Categories.OrderBy(x => x).ToList();
    public IReadOnlyList<string> StandardKinds { get; } = NormativeStandard.Kinds.OrderBy(x => x).ToList();

    public async Task<IActionResult> OnGetAsync()
    {
        Tab = Tab is "standards" or "issues" ? Tab : "records";
        // Deep-link с дашборда: /Admin/Records?filter=issues.
        if (Filter == "issues") Tab = "issues";
        if (PageNumber < 1) PageNumber = 1;

        if (string.IsNullOrEmpty(RegionType) && string.IsNullOrEmpty(RegionCode))
        {
            switch (RegionAlias)
            {
                case "world":
                    RegionType = "world";
                    RegionCode = "";
                    break;
                case "israel":
                    RegionType = "country";
                    RegionCode = "ISR";
                    break;
            }
        }

        if (Tab == "issues")
            Issues = await _quality.ListIssuesAsync(IssueStatus, PageNumber, PageSize);
        else if (Tab == "standards")
            Standards = await _repo.GetStandardsAsync(new StandardFilter(Kind, Gender, PoolType, Style), PageNumber, PageSize);
        else
            Records = await _repo.GetRecordsAsync(new RecordFilter(RegionType, RegionCode, Category, Gender, PoolType, Style), PageNumber, PageSize);

        return Page();
    }
}
