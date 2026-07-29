using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Competitions;

/// <summary>
/// Массовая привязка правил очков к соревнованиям (Э4). Двухшаговая, как MoveResults:
/// GET с фильтрами показывает выборку, POST применяет к отмеченным строкам. Пишет аудит.
///
/// Работает по ДНЯМ, а не по свёрнутым событиям: правило хранится у каждого дня отдельно,
/// и непривязанный день не должен прятаться за головой события.
/// </summary>
[Authorize(Roles = "Admin")]
public class AssignRulesModel : PageModel
{
    private readonly ICompetitionAdminRepository _repo;
    private readonly IPointRulesAdminRepository _rules;
    private readonly IAdminAuditService _audit;

    public AssignRulesModel(
        ICompetitionAdminRepository repo, IPointRulesAdminRepository rules, IAdminAuditService audit)
    {
        _repo = repo;
        _rules = rules;
        _audit = audit;
    }

    [BindProperty(SupportsGet = true, Name = "year")]
    public int? Year { get; set; }

    /// <summary>all | masters | non-masters.</summary>
    [BindProperty(SupportsGet = true, Name = "scope")]
    public string Scope { get; set; } = "all";

    [BindProperty(SupportsGet = true, Name = "unassigned")]
    public bool OnlyUnassigned { get; set; }

    /// <summary>Отмеченные строки (POST).</summary>
    [BindProperty]
    public List<int> Selected { get; set; } = [];

    /// <summary>Что делать с клубным правилом: <c>keep</c> (не менять) | <c>auto</c> (снять привязку) | Id правила.</summary>
    [BindProperty]
    public string ClubsChoice { get; set; } = "keep";

    [BindProperty]
    public string SwimmersChoice { get; set; } = "keep";

    public IReadOnlyList<CompetitionRuleRowDto> Rows { get; private set; } = [];
    public IReadOnlyList<int> Years { get; private set; } = [];
    public IReadOnlyList<PointRuleRowDto> ClubRules { get; private set; } = [];
    public IReadOnlyList<PointRuleRowDto> SwimmerRules { get; private set; } = [];

    public string? Error { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostApplyAsync()
    {
        await LoadAsync();

        if (!TryParseChoice(ClubsChoice, out var setClubs, out var clubsRuleId) ||
            !TryParseChoice(SwimmersChoice, out var setSwimmers, out var swimmersRuleId))
        {
            Error = "Не разобрать выбор правила.";
            return Page();
        }

        var result = await _repo.AssignRulesAsync(new CompetitionRuleAssignmentDto
        {
            CompetitionIds = Selected,
            SetClubs = setClubs,
            ClubsRuleId = clubsRuleId,
            SetSwimmers = setSwimmers,
            SwimmersRuleId = swimmersRuleId
        });

        if (!result.Success)
        {
            Error = result.Error;
            return Page();
        }

        var what = string.Join(", ", new[]
        {
            setClubs ? $"клубное={Describe(clubsRuleId)}" : null,
            setSwimmers ? $"High Point={Describe(swimmersRuleId)}" : null
        }.Where(x => x != null));

        // Id в результате массовой операции — число изменённых строк.
        await _audit.LogAsync("competition.assign-rules", "Competition", "",
            $"Массовая привязка правил очков: {what} для {result.Id} соревнований",
            new { Selected, ClubsChoice, SwimmersChoice });

        TempData["Flash"] = $"Правила проставлены соревнованиям: {result.Id}";
        return RedirectToPage("AssignRules", new { year = Year, scope = Scope, unassigned = OnlyUnassigned });
    }

    /// <summary>«keep» — поле не трогаем; «auto» — сбрасываем в null; число — Id правила.</summary>
    private static bool TryParseChoice(string? choice, out bool set, out int? ruleId)
    {
        set = false;
        ruleId = null;

        if (string.IsNullOrWhiteSpace(choice) || choice == "keep") return true;
        if (choice == "auto") { set = true; return true; }

        if (int.TryParse(choice, out var id))
        {
            set = true;
            ruleId = id;
            return true;
        }

        return false;
    }

    private static string Describe(int? ruleId) => ruleId is int id ? $"#{id}" : "авто";

    private async Task LoadAsync()
    {
        Years = await _repo.GetAvailableYearsAsync();
        ClubRules = await _rules.GetAllAsync(PointRuleKind.Clubs);
        SwimmerRules = await _rules.GetAllAsync(PointRuleKind.Swimmers);
        Rows = await _repo.GetForRuleAssignmentAsync(Year, Scope, OnlyUnassigned);
    }
}
