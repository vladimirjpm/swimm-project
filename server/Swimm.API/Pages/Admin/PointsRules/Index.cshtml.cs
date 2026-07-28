using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.PointsRules;

/// <summary>
/// Список правил начисления очков (Э3): два таба — клубные очки и High Point.
/// CRUD — на странице Edit.
/// </summary>
[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly IPointRulesAdminRepository _repo;

    public IndexModel(IPointRulesAdminRepository repo) => _repo = repo;

    /// <summary>"clubs" | "swimmers" — активный таб (по умолчанию клубный).</summary>
    [BindProperty(SupportsGet = true)]
    public string Kind { get; set; } = "clubs";

    public PointRuleKind RuleKind => PointRulesKindParser.Parse(Kind);

    public IReadOnlyList<PointRuleRowDto> Rules { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Kind = PointRulesKindParser.ToSlug(RuleKind);
        Rules = await _repo.GetAllAsync(RuleKind);
    }
}

/// <summary>Разбор слага таба (<c>clubs</c>/<c>swimmers</c>) — общий для Index и Edit.</summary>
public static class PointRulesKindParser
{
    public static PointRuleKind Parse(string? slug) =>
        string.Equals(slug, "swimmers", StringComparison.OrdinalIgnoreCase)
            ? PointRuleKind.Swimmers
            : PointRuleKind.Clubs;

    public static string ToSlug(PointRuleKind kind) => kind == PointRuleKind.Swimmers ? "swimmers" : "clubs";

    public static string Title(PointRuleKind kind) => kind == PointRuleKind.Swimmers ? "High Point" : "Клубные очки";
}
