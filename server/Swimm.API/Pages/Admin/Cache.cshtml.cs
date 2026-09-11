using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Swimm.API.Pages.Admin;

/// <summary>
/// /Admin/Cache — как устроен кэш и кто сколько кэширует (docs/admin-pages/cache.md).
///
/// Таблица политик собирается ИЗ КОДА: константы Cache-Control и TTL контроллеров читаются
/// рефлексией, поэтому цифры на странице не устаревают сами по себе. Руками поддерживаются
/// только тексты страницы и карта «контроллер → где это на сайте»
/// (<see cref="CachePolicyCatalog.WhereOnSite"/>): контроллер, которого в карте нет, строка
/// показывает с пометкой — это и есть напоминание обновить страницу (docs/pre-push-rules.md).
/// </summary>
[Authorize(Roles = "Admin")]
public class CacheModel : PageModel
{
    public IReadOnlyList<CachePolicyRow> Policies { get; private set; } = [];

    public void OnGet() => Policies = CachePolicyCatalog.Build(typeof(CacheModel).Assembly);
}

/// <summary>Политика кэша одного контроллера: что видит браузер и сколько живёт ответ на сервере.</summary>
public sealed record CachePolicyRow(
    string Controller,
    string? WhereOnSite,
    IReadOnlyList<string> Routes,
    IReadOnlyList<string> BrowserPolicies,
    IReadOnlyList<TimeSpan> ServerTtls)
{
    /// <summary>Самый долгий max-age в секундах; 0 — браузер сверяется каждый раз или не хранит.</summary>
    public int MaxAgeSeconds => BrowserPolicies
        .Select(p => Regex.Match(p, @"max-age=(\d+)"))
        .Where(m => m.Success)
        .Select(m => int.Parse(m.Groups[1].Value))
        .DefaultIfEmpty(0)
        .Max();
}

public static class CachePolicyCatalog
{
    /// <summary>
    /// Где на сайте живут ответы контроллера — единственное, что рефлексия узнать не может.
    /// Новый кэширующий контроллер = строка здесь (иначе на странице он помечен «не описан»).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> WhereOnSite = new Dictionary<string, string>
    {
        ["CategoriesController"] = "названия и бейджи категорий — на всех страницах результатов",
        ["ClubPointsController"] = "клубные очки в таблице результатов",
        ["ClubsPublicController"] = "/clubs/{id} — страница клуба (правится из её таба Admin)",
        ["HubGroupsController"] = "/groups и /groups/{slug} — список и страница группы (правится из её таба Admin)",
        ["RecordsController"] = "справочник рекордов, попап нормативов; дуги уровня и метки рекордов на всех страницах",
        ["ResultsController"] = "/results, /competitions, /competitions/{id} — результаты, список и обзор соревнования",
        ["SeasonBestController"] = "/season-best, бейдж SB в протоколе, Season best у клуба и пловца",
        ["StartListController"] = "таб Start list соревнования, предстоящие старты",
        ["SwimmersPublicController"] = "/swimmers/{id} — страница пловца, head-to-head",
    };

    private const BindingFlags StaticFields =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static IReadOnlyList<CachePolicyRow> Build(Assembly assembly) => assembly.GetTypes()
        .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
        .Select(BuildRow)
        .OfType<CachePolicyRow>()
        .OrderByDescending(r => r.MaxAgeSeconds)
        .ThenBy(r => r.Controller)
        .ToList();

    private static CachePolicyRow? BuildRow(Type controller)
    {
        var fields = controller.GetFields(StaticFields);

        // Политики — строковые константы со значением Cache-Control (у группы их две: общая и
        // приватная). Имя константы не важно — важно, что в ней директива кэша.
        var policies = fields
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(v => v.Contains("max-age") || v.Contains("no-cache") || v.Contains("no-store"))
            .Distinct()
            .ToList();
        if (policies.Count == 0) return null;

        var ttls = fields
            .Where(f => f.IsInitOnly && f.FieldType == typeof(TimeSpan))
            .Select(f => (TimeSpan)f.GetValue(null)!)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        return new CachePolicyRow(
            controller.Name,
            WhereOnSite.GetValueOrDefault(controller.Name),
            GetRoutes(controller),
            policies,
            ttls);
    }

    private static IReadOnlyList<string> GetRoutes(Type controller)
    {
        var name = controller.Name.EndsWith("Controller")
            ? controller.Name[..^"Controller".Length].ToLowerInvariant()
            : controller.Name.ToLowerInvariant();
        var prefix = (controller.GetCustomAttribute<RouteAttribute>()?.Template ?? "")
            .Replace("[controller]", name, StringComparison.OrdinalIgnoreCase)
            .Trim('/');

        return controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpGetAttribute>())
            .Select(a => a.Template switch
            {
                null or "" => "/" + prefix,
                var t when t.StartsWith('/') => t,
                var t => $"/{prefix}/{t}",
            })
            .Distinct()
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>«5 мин», «24 ч», «60 с» — как читает человек.</summary>
    public static string Human(TimeSpan ttl) =>
        ttl.TotalHours >= 1 && ttl.TotalHours % 1 == 0 ? $"{ttl.TotalHours:0} ч"
        : ttl.TotalMinutes >= 1 && ttl.TotalMinutes % 1 == 0 ? $"{ttl.TotalMinutes:0} мин"
        : $"{ttl.TotalSeconds:0} с";
}
