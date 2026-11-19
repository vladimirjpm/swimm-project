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

    public const string AutoPullEnabledKey = "AutoPull:Enabled";

    /// <summary>Файл, из которого ProdAdmin узнаёт, забирает ли прод сам (<see cref="ProdAutoPullEnabled"/>).</summary>
    public const string ProductionSettingsFile = "appsettings.Production.json";

    /// <summary>
    /// ПРОД-РАСПИСАНИЕ забора (docs/plans/prod-auto-pull-plan.md §4.2, решения Р-1, Р-6): задачи
    /// <c>discovery</c> и <c>start-list</c> (<c>CompetitionDiscoveryBackgroundService</c>) идут с
    /// прода круглосуточно, а не из ProdAdmin. По умолчанию ВЫКЛЮЧЕНО везде; включает строка в
    /// <c>appsettings.Production.json</c> — после пробы сети П0 (с App Service до loglig.com:2053).
    /// Выключатели задач в базе (<c>DiscoveryEnabled</c>, <c>StartListEnabled</c>) остаются главными.
    /// </summary>
    public static bool AutoPullEnabled(IConfiguration config) => config.GetValue(AutoPullEnabledKey, false);

    /// <summary>
    /// Забирает ли ПРОД сам — <c>AutoPull:Enabled</c> из закоммиченного <see cref="ProductionSettingsFile"/>
    /// в корне контента. Нужен ProdAdmin (Р-6: одна точка запуска): включили забор на проде — у
    /// ProdAdmin расписание этих двух задач пропадает само, той же одной строкой. Читается файл,
    /// а не переменная окружения App Service: её ProdAdmin не видит, поэтому включать забор надо
    /// именно файлом. Файла нет или он битый — false (ProdAdmin работает как раньше).
    /// </summary>
    public static bool ProdAutoPullEnabled(string contentRootPath)
    {
        try
        {
            var cfg = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(contentRootPath, ProductionSettingsFile), optional: true, reloadOnChange: false)
                .Build();
            return AutoPullEnabled(cfg);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Идёт ли в ЭТОМ процессе расписание <c>discovery</c> + <c>start-list</c>
    /// (<c>CompetitionDiscoveryBackgroundService</c>). Чистое правило — тестируется без хоста:
    /// <list type="bullet">
    /// <item>свой <c>AutoPull:Enabled</c> — да (прод после П0);</item>
    /// <item>иначе — по <c>BackgroundJobs:Enabled</c>, как остальные расписания, НО в ProdAdmin —
    ///   только пока прод сам не забирает (<paramref name="prodAutoPull"/>, Р-6). Флаг выключен →
    ///   ProdAdmin работает ровно как раньше.</item>
    /// </list>
    /// Остальных расписаний ProdAdmin (<c>loglig-verify</c>, <c>loglig-batch</c>, <c>records-check</c>,
    /// <c>swimmer-club-check</c>) правило не касается: они живут в своих службах под одним
    /// <see cref="BackgroundJobsEnabled"/>.
    /// </summary>
    public static bool DiscoveryScheduleEnabled(bool backgroundJobs, bool autoPull, bool isProdAdmin, bool prodAutoPull) =>
        autoPull || (backgroundJobs && !(isProdAdmin && prodAutoPull));

    /// <summary><see cref="DiscoveryScheduleEnabled(bool, bool, bool, bool)"/> по конфигурации процесса.</summary>
    public static bool DiscoveryScheduleEnabled(IConfiguration config, IHostEnvironment env) =>
        DiscoveryScheduleEnabled(
            BackgroundJobsEnabled(config, env),
            AutoPullEnabled(config),
            env.IsProdAdmin(),
            env.IsProdAdmin() && ProdAutoPullEnabled(env.ContentRootPath));

    /// <summary>Строка подключения Application Insights — то же имя, что ищет пакет Azure Monitor.</summary>
    public const string TelemetryConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    public const string TelemetryEnabledKey = "Telemetry:Enabled";

    /// <summary>
    /// Телеметрия в Azure (docs/plans/application-insights-plan.md, А2). Включается НАЛИЧИЕМ строки
    /// подключения: на проде она в настройках App Service, в ProdAdmin — в
    /// <c>appsettings.ProdAdmin.Local.json</c>. Нет строки — ни одного вызова наружу (разработка,
    /// тесты, CI). <c>Telemetry:Enabled=false</c> — аварийный выключатель без удаления строки.
    /// </summary>
    public static bool TelemetryEnabled(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config[TelemetryConnectionStringKey])
        && config.GetValue(TelemetryEnabledKey, true);

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
