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
    private readonly IPointRulesAdminRepository _rules;
    private readonly IAdminAuditService _audit;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(ICompetitionAdminRepository repo, IImportService import,
        IPointRulesAdminRepository rules, IAdminAuditService audit, ILogger<IndexModel> logger)
    {
        _repo = repo;
        _import = import;
        _rules = rules;
        _audit = audit;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "cat")]
    public string? CategoryKey { get; set; }

    /// <summary>Сезон (сен–авг) по году окончания: 2025 = 24/25. Совпадает с cYear на isr.org.il.</summary>
    [BindProperty(SupportsGet = true, Name = "season")]
    public int? Season { get; set; }

    /// <summary>Тип соревнования: «champ» — только чемпионаты (по названию), пусто — все.</summary>
    [BindProperty(SupportsGet = true, Name = "kind")]
    public string? Kind { get; set; }

    /// <summary>
    /// Вид спорта: пусто — только плавание (дефолт), «all» — всё, иначе конкретная дисциплина.
    /// Артистическое плавание из списка прячем, но не удаляем — см. Disciplines.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "discipline")]
    public string? Discipline { get; set; }

    /// <summary>Сколько строк спрятал фильтр дисциплины — чип «скрыто N» над списком.</summary>
    public int HiddenByDiscipline { get; private set; }

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

    /// <summary>Сезоны для фильтра — из справочника И из строк с сайта (прошлые сезоны автозабора).</summary>
    public IReadOnlyList<int> Seasons { get; private set; } = [];

    /// <summary>Правила очков и типы бассейна — для селектов панели быстрой правки строки.</summary>
    public IReadOnlyList<PointRuleRowDto> ClubRules { get; private set; } = [];
    public IReadOnlyList<PointRuleRowDto> SwimmerRules { get; private set; } = [];
    public IReadOnlyList<string> PoolTypeOptions => Swimm.Application.Constants.PoolTypes.All;

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
        var list = await _repo.GetUnifiedAsync(Search, CategoryKey, Season, Stage, ShowSynthetic, Month, PageNumber, PageSize, QualityFilter, Kind, Discipline);
        Result = list.Page;
        MonthCounts = list.MonthCounts;
        HiddenByDiscipline = list.HiddenByDiscipline;
        Categories = await _repo.GetAllCategoriesAsync();
        Seasons = await _repo.GetAvailableSeasonsAsync();
        ClubRules = await _rules.GetAllAsync(PointRuleKind.Clubs);
        SwimmerRules = await _rules.GetAllAsync(PointRuleKind.Swimmers);
    }

    /// <summary>
    /// Быстрая правка из раскрытой панели строки: бассейн, флаги, категории, правила очков —
    /// сразу всем дням события (регламент у события один). Имя/дата/страна правятся на Edit.
    /// </summary>
    public async Task<IActionResult> OnPostQuickSaveAsync(int id, string poolType,
        bool isAward, bool isChampionship, bool showCombineAllResults,
        List<string>? categoryKeys, int? pointRuleClubsId, int? pointRuleSwimmersId,
        bool clubPointsDisabled = false)
    {
        var result = await _repo.QuickUpdateAsync(new CompetitionQuickEditDto
        {
            CompetitionId = id,
            PoolType = poolType ?? "",
            IsAward = isAward,
            IsChampionship = isChampionship,
            ShowCombineAllResults = showCombineAllResults,
            CategoryKeys = categoryKeys ?? [],
            PointRuleClubsId = pointRuleClubsId,
            PointRuleSwimmersId = pointRuleSwimmersId,
            ClubPointsDisabled = clubPointsDisabled
        });

        if (!result.Success)
        {
            TempData["Flash"] = $"Не сохранено: {result.Error}";
            return RedirectToBackToList();
        }

        // Id в результате — число изменённых дней.
        await _audit.LogAsync("competition.quick-edit", "Competition", id.ToString(),
            $"Быстрая правка из списка (#{id}): бассейн={poolType}, awards={isAward}, " +
            $"чемпионат={isChampionship}, combine={showCombineAllResults}, " +
            $"категории=[{string.Join(",", categoryKeys ?? [])}], " +
            $"клубный зачёт={(clubPointsDisabled ? "не ведётся" : "ведётся")}, дней изменено: {result.Id}");

        TempData["Flash"] = result.Id > 1
            ? $"Сохранено, применено ко всем дням события: {result.Id}"
            : "Сохранено";
        return RedirectToBackToList();
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
            q = Search, cat = CategoryKey, season = Season, kind = Kind, stage = Stage,
            synth = ShowSynthetic ? "true" : null, month = Month, p = PageNumber
        });
}
