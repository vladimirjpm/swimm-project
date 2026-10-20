namespace Swimm.API.Hosting;

/// <summary>
/// Форма процесса после решения В17 (docs/plans/azure-deploy-plan.md §0-тер, §0-тер-бис):
/// на проде — только публичный сайт и API, админка и фоновые джобы выключены флагами; админка
/// живёт у Влада локально и в режиме <see cref="ProdAdminEnvironment"/> пишет прямо в прод-БД.
///
/// Админка по умолчанию ВКЛЮЧЕНА, фоновые расписания — включены везде, кроме Development (В10).
/// Выключает их на проде <c>appsettings.Production.json</c> (закоммичен — прод получает выключенную
/// админку сам, даже если в App Service забыли переменную); переменная окружения
/// <c>Admin__Enabled</c> его перебивает.
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
    /// Фоновые РАСПИСАНИЯ — то, что само ходит наружу по таймеру: автозабор isr.org.il (+ проход
    /// стартового протокола), ночная сверка loglig-id, пакетный забор loglig-id.
    /// Явный ключ <c>BackgroundJobs:Enabled</c> важнее всего: прод — false
    /// (<c>appsettings.Production.json</c>), ProdAdmin — true (<c>appsettings.ProdAdmin.json</c>).
    /// Без ключа — false в Development (решение В10, docs/plans/competitions-home-plan.md, часть В:
    /// локальный API не ходит на isr.org.il и loglig при каждом перезапуске; «Входящие» приезжают
    /// синхронизацией с прода или кнопкой) и true в остальных окружениях.
    ///
    /// ОЧЕРЕДИ (импорт, прогон рекордов по странам) от этого флага НЕ зависят — они исполняют то,
    /// что поставила админка, и живут вместе с ней (<see cref="AdminEnabled"/>). Пинг БД и мост
    /// сброса кэша — тоже не зависят.
    /// </summary>
    public static bool BackgroundJobsEnabled(IConfiguration config, IHostEnvironment env) =>
        config.GetValue<bool?>(BackgroundJobsEnabledKey) ?? !env.IsDevelopment();

    public const string PushRemindersEnabledKey = "PushReminders:Enabled";

    /// <summary>
    /// Третий сорт фоновых служб — ПРОД-РАСПИСАНИЕ (docs/plans/web-push-reminders-plan.md §4):
    /// напоминания о тренировках обязаны идти с прода круглосуточно, а ProdAdmin запущен не всегда.
    /// По умолчанию ВЫКЛЮЧЕНО везде; включает <c>appsettings.Production.json</c>. В ProdAdmin
    /// (та же прод-база) и в разработке (локальная база) не включать: в первом случае — дубль
    /// (его всё равно отсечёт UNIQUE журнала доставки), во втором — push живым людям, если в
    /// локальную базу когда-нибудь приедут подписки.
    /// </summary>
    public static bool PushRemindersEnabled(IConfiguration config) => config.GetValue(PushRemindersEnabledKey, false);

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
