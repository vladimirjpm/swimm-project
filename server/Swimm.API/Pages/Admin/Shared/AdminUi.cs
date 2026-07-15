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
        new("Categories", "tag", "/Admin/Categories", "Data"),
        new("Records", "award", "/Admin/Records", "Data"),
        new("HubGroups", "users", "/Admin/HubGroups", "Data"),
        new("Club requests", "inbox", "/Admin/HubGroupClubRequests", "Data"),
        new("Import", "download", "/Admin/Import", "Data"),
        new("Import History", "history", "/Admin/ImportHistory", "Data"),

        new("Users", "users", "/Admin/Users", "System"),
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
/// Модель для partial Shared/_StatusBadge.
/// Variant: success | danger | warning | info | neutral.
/// </summary>
public record StatusBadgeModel(string Text, string Variant = "neutral");
