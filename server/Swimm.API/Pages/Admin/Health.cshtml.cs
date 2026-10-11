using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.API.Pages.Admin.Shared;

namespace Swimm.API.Pages.Admin;

/// <summary>
/// Здоровье данных (docs/data-integrity.md, фаза Д3) — единое место всех проверок
/// целостности. Данные страница берёт через API (/api/admin/data-checks), чтобы прогон
/// не блокировал рендер: проверки ходят в БД десятками запросов.
/// </summary>
[Authorize(Roles = "Admin")]
public class HealthModel(IConfiguration config, IWebHostEnvironment env, IPointRulesAdminRepository rules) : PageModel
{
    /// <summary>База для ссылок «смотреть на сайте» — <see cref="PublicSite.BaseUrl"/>.</summary>
    public string PublicSiteBaseUrl { get; private set; } = "";

    /// <summary>Правила клубных очков — для селекта в находке «без правила клубных очков».
    /// Их единицы, поэтому отдаём страницей, а не отдельным запросом.</summary>
    public IReadOnlyList<PointRuleRowDto> ClubRules { get; private set; } = [];

    public async Task OnGetAsync()
    {
        PublicSiteBaseUrl = PublicSite.BaseUrl(config, env);
        ClubRules = await rules.GetAllAsync(PointRuleKind.Clubs);
    }
}
