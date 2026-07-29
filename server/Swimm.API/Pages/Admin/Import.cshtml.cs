using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ImportModel : PageModel
{
    private readonly IResultSourceProvider _sourceProvider;
    private readonly IPointRulesAdminRepository _rules;

    public ImportModel(IResultSourceProvider sourceProvider, IPointRulesAdminRepository rules)
    {
        _sourceProvider = sourceProvider;
        _rules = rules;
    }

    public IReadOnlyList<string> ParseFormats { get; private set; } = [];

    /// <summary>Правила очков для селектов привязки (Э5): их выбор проставляется
    /// каждому создаваемому соревнованию.</summary>
    public IReadOnlyList<PointRuleRowDto> ClubRules { get; private set; } = [];
    public IReadOnlyList<PointRuleRowDto> SwimmerRules { get; private set; } = [];

    /// <summary>Канонические типы бассейна для select-а (парсер может определить сам — «—» остаётся).</summary>
    public IReadOnlyList<string> PoolTypeOptions => PoolTypes.All;

    /// <summary>Опция DDL языка: код + подпись. Страны — общий каталог <see cref="Shared.CountryCatalog"/>.</summary>
    public record SelectOption(string Code, string Label);

    /// <summary>Языки протокола: he/en — единственные, которые понимает парсер isr.org.il.</summary>
    public static readonly IReadOnlyList<SelectOption> Languages =
    [
        new("he", "HE — иврит"),
        new("en", "EN — английский")
    ];

    public async Task OnGetAsync()
    {
        ParseFormats = _sourceProvider.AvailableFormats;
        ClubRules = await _rules.GetAllAsync(PointRuleKind.Clubs);
        SwimmerRules = await _rules.GetAllAsync(PointRuleKind.Swimmers);
    }
}
