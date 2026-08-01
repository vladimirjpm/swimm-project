using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Competitions;

[Authorize(Roles = "Admin")]
public class EditModel : PageModel
{
    private readonly ICompetitionAdminRepository _repo;
    private readonly IImportService _import;
    private readonly IPointRulesAdminRepository _rules;
    private readonly IAdminAuditService _audit;
    private readonly ILogger<EditModel> _logger;

    public EditModel(
        ICompetitionAdminRepository repo,
        IImportService import,
        IPointRulesAdminRepository rules,
        IAdminAuditService audit,
        ILogger<EditModel> logger)
    {
        _repo = repo;
        _import = import;
        _rules = rules;
        _audit = audit;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    public bool IsNew => Id is null or 0;

    /// <summary>
    /// Куда возвращаться по «← К списку» и после удаления: URL списка с его фильтрами/страницей
    /// (кладёт Index в ссылки «Изменить»). Только локальный URL — открытый редирект недопустим.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "back")]
    public string? BackUrl { get; set; }

    public string BackLink =>
        !string.IsNullOrEmpty(BackUrl) && Url.IsLocalUrl(BackUrl) ? BackUrl : Url.Page("Index")!;

    /// <summary>Редирект к списку — с сохранением фильтров, откуда пришли.</summary>
    private IActionResult RedirectToList() => Redirect(BackLink);

    /// <summary>Редирект на саму форму — back тащим дальше, иначе «К списку» забудет фильтры.</summary>
    private IActionResult RedirectToSelf(int? id) => RedirectToPage("Edit", new { id, back = BackUrl });

    [BindProperty]
    public CompetitionForm Input { get; set; } = new();

    /// <summary>Канонические типы бассейна для select-а формы.</summary>
    public IReadOnlyList<string> PoolTypeOptions => PoolTypes.All;

    /// <summary>Роль соревнования в клубном зачёте, выведенная из текущих флагов, — для подсказки.</summary>
    public string DerivedStandingKindLabel =>
        StandingKinds.Resolve(Input.IsChampionship, Input.PoolType, null) switch
        {
            StandingKinds.Winter => "❄ зимний чемпионат",
            StandingKinds.Summer => "☀ летний чемпионат",
            StandingKinds.OpenWater => "🌊 открытая вода",
            _ => "в зачёт не идёт",
        };

    // Форма добавления URL результатов
    [BindProperty] public string? NewUrlCulture { get; set; }
    [BindProperty] public string? NewUrl { get; set; }

    // Подтверждение каскадного удаления — вводом названия
    [BindProperty] public string? ConfirmName { get; set; }

    /// <summary>Загруженное соревнование (edit-режим): URL-ы, событие, число результатов.</summary>
    public CompetitionEditDto? Existing { get; private set; }

    /// <summary>Все категории (чекбоксы формы).</summary>
    public IReadOnlyList<CategoryTagDto> AllCategories { get; private set; } = [];

    /// <summary>Правила очков для селектов привязки (Э4).</summary>
    public IReadOnlyList<PointRuleRowDto> ClubRules { get; private set; } = [];
    public IReadOnlyList<PointRuleRowDto> SwimmerRules { get; private set; } = [];

    /// <summary>Ошибка валидации мутации — показывается в форме.</summary>
    public string? Error { get; private set; }

    public class CompetitionForm
    {
        public string Name { get; set; } = "";
        public string? SubName { get; set; }
        public string Date { get; set; } = "";
        [Required(ErrorMessage = "Выберите тип бассейна")]
        public string PoolType { get; set; } = "";
        public string Country { get; set; } = "";
        public int? OrgCompId { get; set; }
        public bool IsAward { get; set; }
        /// <summary>Чемпионат Израиля — ручной флаг (галка в форме).</summary>
        public bool IsChampionship { get; set; }
        public bool ShowCombineAllResults { get; set; }
        /// <summary>Роль в клубном зачёте вручную; пусто/null — «Авто» (по IsChampionship + PoolType).</summary>
        public string? StandingKindOverride { get; set; }
        /// <summary>Выбранные категории (IsMasters выводится из категории Masters).</summary>
        public List<string> CategoryKeys { get; set; } = [];
        /// <summary>Правило клубных очков; null — «Авто» (подбор по дате и типу).</summary>
        public int? PointRuleClubsId { get; set; }
        /// <summary>Правило High Point; null — «Авто».</summary>
        public int? PointRuleSwimmersId { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadLookupsAsync();
        if (!IsNew)
        {
            Existing = await _repo.GetByIdAsync(Id!.Value);
            if (Existing == null) return RedirectToList();
            Input = ToForm(Existing);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await LoadLookupsAsync();
        var input = ToInput(Input);
        var result = IsNew
            ? await _repo.CreateAsync(input)
            : await _repo.UpdateAsync(Id!.Value, input);

        if (!result.Success)
        {
            Error = result.Error;
            if (!IsNew) Existing = await _repo.GetByIdAsync(Id!.Value);
            return Page();
        }

        TempData["Flash"] = IsNew ? "Соревнование создано" : "Изменения сохранены";
        return RedirectToSelf(result.Id);
    }

    public async Task<IActionResult> OnPostAddUrlAsync()
    {
        if (IsNew) return RedirectToList();

        await LoadLookupsAsync();
        Existing = await _repo.GetByIdAsync(Id!.Value);
        if (Existing == null) return RedirectToList();

        if (Existing.OrgCompId is not int orgCompId)
        {
            Error = "Сначала задайте OrgCompId и сохраните — URL-ы результатов привязываются к нему.";
            Input = ToForm(Existing);
            return Page();
        }

        var result = await _repo.AddResultUrlAsync(orgCompId, NewUrlCulture ?? "", NewUrl ?? "");
        if (!result.Success)
        {
            Error = result.Error;
            Input = ToForm(Existing);
            return Page();
        }

        TempData["Flash"] = "URL добавлен";
        return RedirectToSelf(Id);
    }

    public async Task<IActionResult> OnPostDeleteUrlAsync(int urlId)
    {
        if (IsNew) return RedirectToList();

        // Удаляем URL только в рамках текущего соревнования (по его OrgCompId), а не по «голому» urlId.
        Existing = await _repo.GetByIdAsync(Id!.Value);
        if (Existing?.OrgCompId is not int orgCompId) return RedirectToSelf(Id);

        await _repo.RemoveResultUrlAsync(urlId, orgCompId);
        TempData["Flash"] = "URL удалён";
        return RedirectToSelf(Id);
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (IsNew) return RedirectToList();

        await LoadLookupsAsync();
        Existing = await _repo.GetByIdAsync(Id!.Value);
        if (Existing == null) return RedirectToList();

        // Каскадное удаление деструктивно → требуем точный ввод названия.
        if (!string.Equals(ConfirmName?.Trim(), Existing.Name.Trim(), StringComparison.Ordinal))
        {
            Error = "Название введено неверно — удаление отменено.";
            Input = ToForm(Existing);
            return Page();
        }

        var deleted = await _import.DeleteCompetitionAsync(Id!.Value);
        if (deleted == null) return RedirectToList();

        _logger.LogWarning(
            "Admin {User} каскадно удалил соревнование #{Id} «{Name}»: {Results} результатов, " +
            "{Relays} эстафет, {Galleries} галерей, {ResultUrls} URL, {ImportHistory} записей истории, {Swimmers} пловцов-сирот",
            User.Identity?.Name ?? "?", deleted.CompetitionId, deleted.CompetitionName,
            deleted.Results, deleted.Relays, deleted.Galleries, deleted.ResultUrls, deleted.ImportHistory, deleted.Swimmers);

        TempData["Flash"] = $"Соревнование «{deleted.CompetitionName}» удалено ({deleted.Results} результатов)";
        return RedirectToList();
    }

    /// <summary>
    /// Проставить выбранные в форме правила очков всем дням события (Э4). Правило хранится
    /// у каждого дня отдельно, а регламент у события один — руками это N однотипных правок.
    /// Остальные поля формы НЕ сохраняются: операция точечная, про правила.
    /// </summary>
    public async Task<IActionResult> OnPostApplyRulesToEventAsync()
    {
        if (IsNew) return RedirectToList();

        await LoadLookupsAsync();
        Existing = await _repo.GetByIdAsync(Id!.Value);
        if (Existing == null) return RedirectToList();

        if (Existing.EventId is not int eventId)
        {
            Error = "Соревнование не входит в многодневное событие.";
            Input = ToForm(Existing);
            return Page();
        }

        var dayIds = await _repo.GetEventDayIdsAsync(eventId);
        var result = await _repo.AssignRulesAsync(new CompetitionRuleAssignmentDto
        {
            CompetitionIds = dayIds,
            SetClubs = true,
            ClubsRuleId = Input.PointRuleClubsId,
            SetSwimmers = true,
            SwimmersRuleId = Input.PointRuleSwimmersId
        });

        if (!result.Success)
        {
            Error = result.Error;
            Input = ToForm(Existing);
            return Page();
        }

        // Id в результате массовой операции — число изменённых строк.
        await _audit.LogAsync("competition.assign-rules", "CompetitionEvent", eventId.ToString(),
            $"Правила очков проставлены всем дням события #{eventId}: клубное={Describe(Input.PointRuleClubsId)}, " +
            $"High Point={Describe(Input.PointRuleSwimmersId)} ({result.Id} дней)");

        TempData["Flash"] = $"Правила проставлены дням события: {result.Id}";
        return RedirectToSelf(Id);
    }

    private static string Describe(int? ruleId) => ruleId is int id ? $"#{id}" : "авто";

    private async Task LoadLookupsAsync()
    {
        AllCategories = await _repo.GetAllCategoriesAsync();
        ClubRules = await _rules.GetAllAsync(PointRuleKind.Clubs);
        SwimmerRules = await _rules.GetAllAsync(PointRuleKind.Swimmers);
    }

    private static CompetitionForm ToForm(CompetitionEditDto d) => new()
    {
        Name = d.Name,
        SubName = d.SubName,
        Date = d.Date,
        PoolType = d.PoolType,
        Country = d.Country,
        OrgCompId = d.OrgCompId,
        IsAward = d.IsAward,
        IsChampionship = d.IsChampionship,
        ShowCombineAllResults = d.ShowCombineAllResults,
        StandingKindOverride = d.StandingKindOverride,
        CategoryKeys = d.CategoryKeys,
        PointRuleClubsId = d.PointRuleClubsId,
        PointRuleSwimmersId = d.PointRuleSwimmersId
    };

    private static CompetitionInputDto ToInput(CompetitionForm f) => new()
    {
        Name = f.Name,
        SubName = f.SubName,
        Date = f.Date,
        PoolType = f.PoolType,
        Country = f.Country,
        OrgCompId = f.OrgCompId,
        IsAward = f.IsAward,
        IsChampionship = f.IsChampionship,
        ShowCombineAllResults = f.ShowCombineAllResults,
        StandingKindOverride = f.StandingKindOverride,
        CategoryKeys = f.CategoryKeys ?? [],
        PointRuleClubsId = f.PointRuleClubsId,
        PointRuleSwimmersId = f.PointRuleSwimmersId
    };
}
