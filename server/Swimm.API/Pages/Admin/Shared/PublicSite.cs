namespace Swimm.API.Pages.Admin.Shared;

/// <summary>
/// Ссылки «смотреть на сайте» из админки — одно место на все страницы (раньше Competitions и
/// Health считали базу каждая своей копией).
///
/// База: в проде клиент лежит на том же origin, что и админка, поэтому пусто = относительные
/// ссылки. В Development клиент крутится на своём Vite-порту, и по умолчанию это :5173 — иначе
/// ссылка вела бы в админку, где публичных страниц нет. Переопределяется настройкой
/// <c>PublicSite:BaseUrl</c> (без завершающего «/»). Хранить базу в данных нельзя: dev-адрес
/// осел бы в БД и уехал в прод.
///
/// Пути — зеркало контракта клиента <c>client/src/utils/routes.ts</c>: меняешь маршрут там —
/// меняй и здесь (правило «три зеркала маршрута», docs/pre-push-rules.md).
/// </summary>
public static class PublicSite
{
    public static string BaseUrl(IConfiguration config, IWebHostEnvironment env) =>
        (config["PublicSite:BaseUrl"] ?? (env.IsDevelopment() ? "http://localhost:5173" : ""))
        .TrimEnd('/');

    /// <summary><c>routes.group(slug)</c> — страница группы.</summary>
    public static string GroupPath(string slug) => $"/groups/{Uri.EscapeDataString(slug)}";

    /// <summary><c>routes.swimmer(id)</c> — страница пловца.</summary>
    public static string SwimmerPath(int id) => $"/swimmers/{id}";
}
