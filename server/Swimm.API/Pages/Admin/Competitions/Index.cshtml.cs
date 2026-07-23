using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Competitions;

/// <summary>
/// Соревнования: объединённый список справочника БД (Competitions) и входящих isr.org.il
/// (Discovery) одной таблицей со стадией жизненного цикла. Заменил обе прежние страницы
/// (Competitions/Index + Discovery). Действия входящих — через /api/admin/discovery/*
/// (клиентский JS); CRUD БД — здесь (Edit-страница + каскадное удаление).
/// </summary>
[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private const int PageSize = 20;
    private readonly ICompetitionAdminRepository _repo;
    private readonly IImportService _import;
    private readonly IAdminAuditService _audit;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ICompetitionAdminRepository repo, IImportService import,
        IAdminAuditService audit, ILogger<IndexModel> logger)
    {
        _repo = repo;
        _import = import;
        _audit = audit;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "cat")]
    public string? CategoryKey { get; set; }

    [BindProperty(SupportsGet = true, Name = "year")]
    public int? Year { get; set; }

    /// <summary>Фильтр по стадии: OnSite | Imported | DbOnly | Ignored (пусто — все).</summary>
    [BindProperty(SupportsGet = true, Name = "stage")]
    public string? Stage { get; set; }

    /// <summary>
    /// Deep-link алиас с дашборда: ignored → Stage=Ignored, discovery-new → Stage=OnSite
    /// (только если Stage не задан явно явным параметром). Остальные значения (discovery-error,
    /// no-org-comp-id, no-results) — T3b, идут в <see cref="QualityFilter"/> доп-WHERE'ом.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "filter")]
    public string? FilterAlias { get; set; }

    /// <summary>Разобранный из FilterAlias качественный фильтр (T3b) — для шапки списка/ссылки «сбросить».</summary>
    public string? QualityFilter { get; private set; }

    /// <summary>Показывать тестовую синтетику (SYNTH Meet…). По умолчанию скрыта.</summary>
    [BindProperty(SupportsGet = true, Name = "synth")]
    public bool ShowSynthetic { get; set; }

    /// <summary>Фильтр по месяцу 1–12 (null — все). Кнопки-месяцы над списком.</summary>
    [BindProperty(SupportsGet = true, Name = "month")]
    public int? Month { get; set; }

    // ВНИМАНИЕ: имя параметра — «p», а НЕ «page»: «page» зарезервировано роутингом Razor Pages
    // (и для Url.Page-генерации, и для байндинга), из-за чего пагинация молча ломается.
    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public PagedResult<UnifiedCompetitionRowDto> Result { get; private set; } =
        new([], 0, 1, PageSize);

    /// <summary>Счётчики соревнований по месяцам (индекс 0 = январь) для кнопок-фильтров.</summary>
    public IReadOnlyList<int> MonthCounts { get; private set; } = new int[12];

    public IReadOnlyList<CategoryTagDto> Categories { get; private set; } = [];

    public IReadOnlyList<int> Years { get; private set; } = [];

    public async Task OnGetAsync()
    {
        if (PageNumber < 1) PageNumber = 1;
        if (string.IsNullOrEmpty(Stage))
        {
            Stage = FilterAlias switch
            {
                "ignored" => "Ignored",
                "discovery-new" => "OnSite",
                _ => Stage
            };
        }
        QualityFilter = FilterAlias switch
        {
            "discovery-error" or "no-org-comp-id" or "no-results" => FilterAlias,
            _ => null
        };
        var list = await _repo.GetUnifiedAsync(Search, CategoryKey, Year, Stage, ShowSynthetic, Month, PageNumber, PageSize, QualityFilter);
        Result = list.Page;
        MonthCounts = list.MonthCounts;
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

        await _audit.LogAsync("competition.delete", "Competition", deleted.CompetitionId.ToString(),
            $"Каскадно удалено соревнование «{deleted.CompetitionName}» ({deleted.Results} результатов)",
            deleted);

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

        await _audit.LogAsync("competition.delete-event", "CompetitionEvent", eventId.ToString(),
            $"Каскадно удалено многодневное событие «{deleted.CompetitionName}» ({deleted.Results} результатов)",
            deleted);

        TempData["Flash"] = $"Событие «{deleted.CompetitionName}» удалено вместе со всеми днями ({deleted.Results} результатов)";
        return RedirectToBackToList();
    }

    // Сохраняем текущие фильтры/страницу при возврате к списку.
    private IActionResult RedirectToBackToList() =>
        RedirectToPage("Index", new
        {
            q = Search, cat = CategoryKey, year = Year, stage = Stage,
            synth = ShowSynthetic ? "true" : null, month = Month, p = PageNumber
        });
}
