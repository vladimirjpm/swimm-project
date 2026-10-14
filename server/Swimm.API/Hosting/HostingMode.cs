namespace Swimm.API.Hosting;

/// <summary>
/// Форма процесса после решения В17 (docs/plans/azure-deploy-plan.md §0-тер, §0-тер-бис):
/// на проде — только публичный сайт и API, админка и фоновые джобы выключены флагами; админка
/// живёт у Влада локально и в режиме <see cref="ProdAdminEnvironment"/> пишет прямо в прод-БД.
///
/// Флаги по умолчанию ВКЛЮЧЕНЫ: в разработке ничего не меняется. Выключает их
/// <c>appsettings.Production.json</c> (закоммичен — прод получает выключенную админку сам, даже
/// если в App Service забыли переменную); переменная окружения <c>Admin__Enabled</c> его перебивает.
/// </summary>
public static class HostingMode
{
    /// <summary>
    /// Окружение «локальная админка против прод-БД». НЕ Development: в нём нет обхода логина
    /// (DevAdminBypass), тестовых персонажей (--seed-personas) и ручек /api/dev/*.
    /// </summary>
    public const string ProdAdminEnvironment = "ProdAdmin";

    public const string AdminEnabledKey = "Admin:Enabled";
    public const string BackgroundJobsEnabledKey = "BackgroundJobs:Enabled";

    public static bool IsProdAdmin(this IHostEnvironment env) => env.IsEnvironment(ProdAdminEnvironment);

    /// <summary>
    /// Процесс крутится на машине Влада по http://localhost — разработка или ProdAdmin. Таким
    /// не нужны HSTS и редирект на https, а кука без Secure (SameAsRequest) — иначе локальный
    /// вход по http не работает.
    /// </summary>
    public static bool IsLocalMachine(this IHostEnvironment env) => env.IsDevelopment() || env.IsProdAdmin();

    /// <summary>Админка (/Admin/*, /api/admin/*, admin-home.html) включена. На проде — false.</summary>
    public static bool AdminEnabled(IConfiguration config) => config.GetValue(AdminEnabledKey, true);

    /// <summary>
    /// Фоновые джобы: очередь импорта, прогон рекордов по странам, автозабор isr.org.il,
    /// ночная сверка loglig-id, пакетный забор. На проде — false: все обращения к loglig и долгие
    /// операции идут из локальной админки. Пинг БД и мост сброса кэша от флага НЕ зависят.
    /// </summary>
    public static bool BackgroundJobsEnabled(IConfiguration config) => config.GetValue(BackgroundJobsEnabledKey, true);

    /// <summary>
    /// Путь принадлежит админке. Регистр не важен (<see cref="PathString.StartsWithSegments(PathString)"/>
    /// сравнивает без учёта регистра): <c>/ADMIN</c> не должен проскочить мимо выключателя.
    /// Скрипты админки (<c>/js/admin/*</c>) секретов не несут, но без админки им отвечать незачем.
    /// </summary>
    public static bool IsAdminPath(PathString path) =>
        path.StartsWithSegments("/admin")
        || path.StartsWithSegments("/api/admin")
        || path.StartsWithSegments("/js/admin")
        || path.StartsWithSegments("/admin-home.html");
}
