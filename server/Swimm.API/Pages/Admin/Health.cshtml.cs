using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin;

/// <summary>
/// Здоровье данных (docs/data-integrity.md, фаза Д3) — единое место всех проверок
/// целостности. Данные страница берёт через API (/api/admin/data-checks), чтобы прогон
/// не блокировал рендер: проверки ходят в БД десятками запросов.
/// </summary>
[Authorize(Roles = "Admin")]
public class HealthModel(IConfiguration config, IWebHostEnvironment env, IPointRulesAdminRepository rules) : PageModel
{
    /// <summary>
    /// База для ссылок «смотреть на сайте». В проде клиент лежит на том же origin, что и
    /// админка, поэтому пусто = относительные ссылки. В Development клиент крутится на своём
    /// Vite-порту, и по умолчанию это :5173 — иначе ссылка вела бы в админку, где публичных
    /// страниц нет. Переопределяется настройкой <c>PublicSite:BaseUrl</c> (без завершающего «/»).
    ///
    /// Хранить базу в самой находке нельзя: dev-адрес осел бы в БД и уехал в прод.
    /// </summary>
    public string PublicSiteBaseUrl { get; private set; } = "";

    /// <summary>Правила клубных очков — для селекта в находке «без правила клубных очков».
    /// Их единицы, поэтому отдаём страницей, а не отдельным запросом.</summary>
    public IReadOnlyList<PointRuleRowDto> ClubRules { get; private set; } = [];

    public async Task OnGetAsync()
    {
        PublicSiteBaseUrl = (config["PublicSite:BaseUrl"]
            ?? (env.IsDevelopment() ? "http://localhost:5173" : ""))
            .TrimEnd('/');
        ClubRules = await rules.GetAllAsync(PointRuleKind.Clubs);
    }
}
