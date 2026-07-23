namespace Swimm.API.Pages.Admin.Shared;

/// <summary>
/// Пункт навигации сайдбара. <paramref name="ExactMatch"/> — подсвечивать только при
/// точном совпадении пути (для /Admin, иначе он «активен» на всех страницах).
/// </summary>
public record AdminNavItem(string Title, string Icon, string Url, string Section = "", bool ExactMatch = false);

/// <summary>
/// Навигация админки. Добавление новой страницы = одна строка здесь + новая Razor Page.
/// </summary>
public static class AdminNav
{
    public static readonly IReadOnlyList<AdminNavItem> Items =
    [
        new("Dashboard", "gauge", "/Admin", ExactMatch: true),

        new("Competitions", "trophy", "/Admin/Competitions", "Data"),
        new("Results", "award", "/Admin/Results", "Data"),
        new("Categories", "tag", "/Admin/Categories", "Data"),
        new("Styles", "waves", "/Admin/Styles", "Data"),
        new("Records", "award", "/Admin/Records", "Data"),
        new("HubGroups", "users", "/Admin/HubGroups", "Data"),
        new("Club requests", "inbox", "/Admin/HubGroupClubRequests", "Data"),
        new("Import", "download", "/Admin/Import", "Data"),
        new("Swimmers", "users", "/Admin/Swimmers", "Data"),
        new("Loglig ID", "users", "/Admin/Swimmers/Loglig", "Data"),
        new("Clubs", "shield", "/Admin/Clubs", "Data"),
        new("Import History", "history", "/Admin/ImportHistory", "Data"),

        new("Media", "alert-triangle", "/Admin/Media", "System"),

        new("Users", "users", "/Admin/Users", "System"),
        new("Audit log", "history", "/Admin/Audit", "System"),
        new("DB Schema", "database", "/Admin/Db", "System"),
        new("API Reference", "radio", "/Admin/Api", "System"),
        new("Settings", "settings", "/Admin/Settings", "System"),
    ];

    /// <summary>Человекочитаемые заголовки секций сайдбара (пустая секция — без заголовка).</summary>
    public static readonly IReadOnlyDictionary<string, string> SectionTitles = new Dictionary<string, string>
    {
        [""] = "",
        ["Data"] = "Данные",
        ["System"] = "Система",
    };
}

/// <summary>Модель для partial Shared/_PageHeader.</summary>
public record PageHeaderModel(string Title, string? Subtitle = null);

/// <summary>
/// Модель для partial Shared/_Pagination — единый блок «← Назад · Страница X из Y · Вперёд →».
/// URL соседних страниц каждый список считает сам (параметры фильтров разные): передаётся
/// готовая ссылка либо null (край/недоступно → неактивная кнопка). Блок не рендерится при
/// <c>TotalPages &lt;= 1</c>.
/// </summary>
public record PaginationModel(int Page, int TotalPages, string? PrevUrl, string? NextUrl);

/// <summary>
/// Модель для partial Shared/_StatusBadge.
/// Variant: success | danger | warning | info | neutral.
/// </summary>
public record StatusBadgeModel(string Text, string Variant = "neutral");
