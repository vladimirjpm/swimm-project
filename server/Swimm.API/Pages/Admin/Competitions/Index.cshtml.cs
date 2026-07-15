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
    private readonly IImportService _import;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ICompetitionAdminRepository repo, IImportService import, ILogger<IndexModel> logger)
    {
        _repo = repo;
        _import = import;
        _logger = logger;
    }

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

    /// <summary>Каскадное удаление одного соревнования (одиночного или отдельного дня события).</summary>
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var deleted = await _import.DeleteCompetitionAsync(id);
        if (deleted == null)
        {
            TempData["Flash"] = "Соревнование не найдено — возможно, уже удалено.";
            return RedirectToBackToList();
        }

        _logger.LogWarning(
            "Admin {User} каскадно удалил соревнование #{Id} «{Name}»: {Results} результатов, " +
            "{Relays} эстафет, {Galleries} галерей, {ResultUrls} URL, {ImportHistory} записей истории, {Swimmers} пловцов-сирот",
            User.Identity?.Name ?? "?", deleted.CompetitionId, deleted.CompetitionName,
            deleted.Results, deleted.Relays, deleted.Galleries, deleted.ResultUrls, deleted.ImportHistory, deleted.Swimmers);

        TempData["Flash"] = $"Соревнование «{deleted.CompetitionName}» удалено ({deleted.Results} результатов)";
        return RedirectToBackToList();
    }

    /// <summary>Каскадное удаление всего многодневного события: все дни + сам CompetitionEvent.</summary>
    public async Task<IActionResult> OnPostDeleteEventAsync(int eventId)
    {
        var deleted = await _import.DeleteCompetitionEventAsync(eventId);
        if (deleted == null)
        {
            TempData["Flash"] = "Событие не найдено — возможно, уже удалено.";
            return RedirectToBackToList();
        }

        _logger.LogWarning(
            "Admin {User} каскадно удалил многодневное событие #{Id} «{Name}»: {Results} результатов, " +
            "{Relays} эстафет, {Galleries} галерей, {ResultUrls} URL, {ImportHistory} записей истории, {Swimmers} пловцов-сирот",
            User.Identity?.Name ?? "?", deleted.CompetitionId, deleted.CompetitionName,
            deleted.Results, deleted.Relays, deleted.Galleries, deleted.ResultUrls, deleted.ImportHistory, deleted.Swimmers);

        TempData["Flash"] = $"Событие «{deleted.CompetitionName}» удалено вместе со всеми днями ({deleted.Results} результатов)";
        return RedirectToBackToList();
    }

    // Сохраняем текущие фильтры/страницу при возврате к списку.
    private IActionResult RedirectToBackToList() =>
        RedirectToPage("Index", new { q = Search, cat = CategoryKey, year = Year, page = PageNumber });
}
