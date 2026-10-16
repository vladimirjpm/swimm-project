using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Swimm.Application.Abstractions;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Swimm.Infrastructure.Services.DataChecks;

namespace Swimm.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Least-privilege: разные DB-роли для разных путей.
        //   AdminConnection (swimm_rw) — рантайм-запись (auth, admin, импорт).
        //   ReadConnection  (swimm_ro) — публичный read-путь (browsing результатов).
        // Если выделенная строка не задана — graceful fallback на DefaultConnection
        // (одна роль, без изоляции): дев и не настроенные окружения продолжают работать.
        var defaultCs = configuration.GetConnectionString("DefaultConnection");
        var adminCs = configuration.GetConnectionString("AdminConnection") ?? defaultCs;
        var readCs = configuration.GetConnectionString("ReadConnection") ?? defaultCs;

        // Перехватчик ставит метки кэша сам: SQL, выполненный во время сборки записи кэша,
        // отмечает в ней таблицы, которых коснулся (К3, docs/plans/cache-tags-plan.md). Он
        // stateless — один экземпляр на оба контекста. Контекст, собранный в обход DI, меток
        // не ставит: такие есть только у миграций и dotnet ef, кэш там не строится.
        var cacheDependencies = new CacheDependencyInterceptor();

        // Сброс — тоже сам (К4): сохранение через EF сбрасывает метки изменённых таблиц, после
        // коммита. Только у контекста записи — контекст чтения ничего не сохраняет. Синглтон:
        // держит ICacheService, а состояние сохранения — по экземпляру контекста.
        services.AddSingleton<CacheInvalidationInterceptor>();

        services.AddDbContext<SwimmDbContext>((sp, options) => options
            .UseNpgsql(adminCs, npgsql =>
                npgsql.EnableRetryOnFailure(maxRetryCount: 3))
            .AddInterceptors(cacheDependencies, sp.GetRequiredService<CacheInvalidationInterceptor>()));

        services.AddDbContext<SwimmReadDbContext>(options => options
            .UseNpgsql(readCs, npgsql =>
                npgsql.EnableRetryOnFailure(maxRetryCount: 3))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .AddInterceptors(cacheDependencies));

        // Кэш — IMemoryCache для одного инстанса; для Redis заменить MemoryCacheService
        // на RedisCacheService (IDistributedCache + JSON) без изменений в потребителях.
        // Один экземпляр под двумя интерфейсами: диагностика (/Admin/Cache) смотрит в тот же кэш.
        services.AddMemoryCache();
        services.AddSingleton(sp => new MemoryCacheService(
            sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetService<ILogger<MemoryCacheService>>(),
            // Сторож меток (К5): запись без меток данных в Development роняет запрос — ошибку
            // видит тот, кто завёл эндпоинт; на проде и вне хоста — предупреждение в лог.
            untagged: sp.GetService<IHostEnvironment>()?.IsDevelopment() == true
                ? UntaggedEntryPolicy.Throw
                : UntaggedEntryPolicy.Warn));
        services.AddSingleton<ICacheDiagnostics>(sp => sp.GetRequiredService<MemoryCacheService>());

        // Мост сброса кэша между процессами (В17): сброс у себя → pg_notify → чужие процессы
        // сбрасывают у себя (Services/CacheBridge.cs). Включён по умолчанию — и в разработке:
        // CLI-команда, запущенная рядом с живым API, тоже должна сбрасывать его кэш.
        // Выключатель — CacheBridge:Enabled=false; тогда ICacheService — голый MemoryCacheService.
        var bridge = new CacheBridgeOptions(
            configuration.GetValue("CacheBridge:Enabled", true),
            configuration["CacheBridge:Channel"] is { Length: > 0 } ch ? ch : CacheBridgeOptions.DefaultChannel);
        if (!CacheBridgeOptions.IsValidChannel(bridge.Channel))
            throw new InvalidOperationException($"CacheBridge:Channel «{bridge.Channel}» — допустимы только [a-z0-9_].");
        services.AddSingleton(bridge);
        services.AddSingleton<CacheBridgeMonitor>();
        services.AddSingleton<ICacheBridgeStatus>(sp => sp.GetRequiredService<CacheBridgeMonitor>());
        if (bridge.Enabled)
        {
            // Отправка — от роли записи (её соединение и так есть у писателя), коротким таймаутом:
            // запись уже закоммичена, ждать недоступный канал долго незачем.
            services.AddSingleton<ICacheBridgeTransport>(_ =>
                new PgNotifyTransport(BridgeDataSource(adminCs, timeoutSeconds: 3), bridge.Channel));
            services.AddSingleton<ICacheService>(sp => new BridgingCacheService(
                sp.GetRequiredService<MemoryCacheService>(),
                sp.GetRequiredService<ICacheBridgeTransport>(),
                sp.GetRequiredService<CacheBridgeMonitor>(),
                sp.GetService<ILogger<BridgingCacheService>>()));
            // Слушатель — всегда, когда мост включён, и от BackgroundJobs:Enabled не зависит: на
            // проде это единственный способ узнать о записи локальной админки. В CLI хост не
            // стартует, и слушатель не запускается — CLI только отправляет.
            // Слушать может любая роль (LISTEN прав не требует) — берём наименее привилегированную.
            services.AddHostedService(sp => new CacheBridgeListener(
                BridgeDataSource(readCs, timeoutSeconds: 10),
                sp.GetRequiredService<MemoryCacheService>(),
                sp.GetRequiredService<CacheBridgeMonitor>(),
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<ILogger<CacheBridgeListener>>()));
        }
        else
            services.AddSingleton<ICacheService>(sp => sp.GetRequiredService<MemoryCacheService>());

        // Отстаёт ли локальная копия от прода (В17, §0-тер-бис). Включается строкой
        // ProdVersionConnection (роль swimm_sync) в gitignored-конфиге разработки; без неё — молчит.
        services.AddSingleton<IDataSyncStatus>(_ => new DataSyncStatusService(
            configuration.GetConnectionString("ProdVersionConnection"), adminCs));

        // Настройки /Admin/Settings: реестр и дефолты — в коде, изменённые значения — в БД (В7,
        // Sys_AdminSettings). Среда нужна дефолтам отладочного толка (доля сверки кэша); вне хоста
        // (тесты) IHostEnvironment нет — тогда дефолты как на проде.
        services.AddSingleton<IAdminSettingsStore, DbAdminSettingsStore>();
        services.AddSingleton<ISettingsService>(sp => new AdminSettingsService(
            sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
            development: sp.GetService<IHostEnvironment>()?.IsDevelopment() ?? false,
            store: sp.GetRequiredService<IAdminSettingsStore>(),
            logger: sp.GetService<ILogger<AdminSettingsService>>()));
        services.AddScoped<IDebugOptionsService, DebugOptionsService>();

        // Scoped services
        services.AddScoped<IImportService, JsonImportService>();
        services.AddScoped<ISchemaService, DbSchemaService>();

        // Import job queue — singleton, разделяется между контроллером и BackgroundService
        services.AddSingleton<ImportJobQueue>();
        services.AddSingleton<IImportJobQueue>(sp => sp.GetRequiredService<ImportJobQueue>());

        // Прогон рекордов по странам (11.1.2): очередь — singleton, сам ход прогона — scoped
        // (дифф ходит в БД). Пауза между странами по умолчанию, тесты подставляют свою.
        services.AddSingleton<RecordCountryRunQueue>();
        services.AddSingleton<IRecordCountryRunQueue>(sp => sp.GetRequiredService<RecordCountryRunQueue>());
        // Архив источников: боевой прогон сам кладёт выгрузку разобранных строк в
        // !records-sources/ (правило 11 pre-push). Путь из конфига, относительный считается от
        // ContentRoot; пустой — архив выключен. Вне Development относительный путь тоже
        // выключает архив — см. FileRecordRunArchive.ResolveDirectory.
        // ProdAdmin (В17) — тоже машина Влада с репозиторием: боевой прогон по странам идёт оттуда.
        services.AddSingleton<IRecordRunArchive>(sp =>
        {
            var env = sp.GetRequiredService<IHostEnvironment>();
            var dir = FileRecordRunArchive.ResolveDirectory(
                configuration["RecordsImport:SourceArchiveDir"], env.ContentRootPath, HostingEnvironments.IsLocalMachine(env));

            return new FileRecordRunArchive(
                dir, sp.GetRequiredService<ILogger<FileRecordRunArchive>>());
        });

        services.AddScoped(sp => new RecordCountryRunner(
            sp.GetRequiredService<IRecordCountriesProvider>(),
            sp.GetRequiredService<IRecordCountryFetcher>(),
            sp.GetRequiredService<IRecordDiffService>(),
            sp.GetRequiredService<IRecordRunArchive>()));

        // Repositories
        services.AddScoped<IResultRepository, ResultRepository>();
        services.AddScoped<IRecordRepository, RecordRepository>();
        services.AddScoped<IRecordAdminRepository, RecordAdminRepository>();
        services.AddScoped<IRecordDiffService, RecordDiffService>();
        services.AddScoped<IRecordSourceCheckService, RecordSourceCheckService>();
        services.AddScoped<IAdminRepository, AdminRepository>();
        services.AddScoped<ICompetitionAdminRepository, CompetitionAdminRepository>();
        services.AddScoped<ICategoryAdminRepository, CategoryAdminRepository>();
        services.AddScoped<IStyleAdminRepository, StyleAdminRepository>();
        services.AddScoped<IPointRulesAdminRepository, PointRulesAdminRepository>();
        services.AddScoped<IResultAdminRepository, ResultAdminRepository>();
        services.AddScoped<IResultTransferService, ResultTransferService>();
        // Деление схлопнутой многодневки по дням без переимпорта (multiday-split-plan, М2).
        services.AddScoped<ICompetitionDaySplitService, CompetitionDaySplitService>();
        // Обзор: кто ещё схлопнут и чьи дни не собраны в событие (multiday-split-plan, М4).
        services.AddScoped<ICollapsedDaysReviewService, CollapsedDaysReviewService>();
        // Сбор отдельных соревнований в событие — «дни есть, события нет» (там же, М4).
        services.AddScoped<ICompetitionDayJoinService, CompetitionDayJoinService>();
        services.AddScoped<IClubAdminRepository, ClubAdminRepository>();
        services.AddScoped<IClubPublicRepository, ClubPublicRepository>();
        services.AddScoped<IStartListPublicRepository, StartListPublicRepository>();
        services.AddScoped<IClubOverviewRepository, ClubOverviewRepository>();
        services.AddScoped<ISeasonBestRepository, SeasonBestRepository>();
        // Витринный сезон — ОДИН на продукт (docs/season-boundary-rule.md): его спрашивают
        // и клуб, и страница пловца, и /season-best. Копия расчёта в любом из них снова
        // разъедется с правилом — так и появился баг 2026-09-01.
        services.AddScoped<IShowcaseSeasonProvider, ShowcaseSeasonProvider>();
        services.AddScoped<ISwimmerPageRepository, SwimmerPageRepository>();
        services.AddScoped<IUserFavoriteRepository, UserFavoriteRepository>();
        // Персональный план на соревнование в табе Start list (Т3)
        services.AddScoped<IStartListPlanRepository, StartListPlanRepository>();
        services.AddScoped<IUserMediaRepository, UserMediaRepository>();
        services.AddScoped<IMySwimsRepository, MySwimsRepository>();
        services.AddScoped<IReactionRepository, ReactionRepository>();
        services.AddScoped<IUserMediaPublicationService, UserMediaPublicationService>();
        services.AddScoped<IMediaReportService, MediaReportService>();
        services.AddScoped<HubGroupCrudCore>();
        services.AddScoped<IHubGroupAdminService, HubGroupAdminService>();
        services.AddScoped<IHubGroupPublicRepository, HubGroupPublicRepository>();
        services.AddScoped<IHubGroupPermissionService, HubGroupPermissionService>();
        services.AddScoped<IEntityDisplayRepository, EntityDisplayRepository>();
        services.AddScoped<IHubGroupUserService, HubGroupUserService>();
        // «Set up profile» (docs/plans/profile-setup-plan.md) — личное, без кэша.
        services.AddScoped<IProfileSetupService, ProfileSetupService>();
        services.AddScoped<IHubGroupClubRequestAdminService, HubGroupClubRequestAdminService>();
        // Подписка группы на клуб: зовут контроллер «My groups», импорт (после коммита) и
        // склейка клубов — оба последних через необязательный параметр конструктора.
        services.AddScoped<IHubGroupClubSubscriptionService, HubGroupClubSubscriptionService>();
        // Уровни пловцов группы (docs/plans/lane-plans-plan.md) — приватные, без кэша.
        services.AddScoped<IHubGroupLevelService, HubGroupLevelService>();
        services.AddScoped<ILanePlanService, LanePlanService>();
        services.AddScoped<ITrainingRsvpService, TrainingRsvpService>();
        services.AddScoped<ITrainingSessionService, TrainingSessionService>();
        services.AddScoped<IHubGroupBreakService, HubGroupBreakService>();
        services.AddScoped<IClubPointsRepository, ClubPointsRepository>();
        services.AddScoped<IClubStandingService, ClubStandingService>();
        services.AddScoped<ICompetitionRecalculationService, CompetitionRecalculationService>();
        services.AddScoped<IImportAuditService, ImportAuditService>();
        services.AddScoped<IImportRecordPreviewService, ImportRecordPreviewService>();

        // Реестр проверок данных (Д3, docs/data-integrity.md). Проверки регистрируются как
        // IDataCheck — добавить новую значит добавить строку сюда, ничего больше не трогая.
        services.AddScoped<IDataCheckRunner, DataCheckRunner>();
        services.AddScoped<IDataCheck, ExactDuplicateCheck>();
        services.AddScoped<IDataCheck, UpsertKeyCollisionCheck>();
        services.AddScoped<IDataCheck, RelayGenderFromLegCheck>();
        services.AddScoped<IDataCheck, DuplicateEventDayCheck>();
        services.AddScoped<IDataCheck, EmptyCompetitionCheck>();
        services.AddScoped<IDataCheck, RelayDistanceWithoutRelayCheck>();
        services.AddScoped<IDataCheck, FkAnomalyCheck>();
        services.AddScoped<IDataCheck, ReconciliationMismatchCheck>();
        services.AddScoped<IDataCheck, NoGenderCheck>();
        services.AddScoped<IDataCheck, GenderVsCardCheck>();
        services.AddScoped<IDataCheck, EmptyRelayCheck>();
        services.AddScoped<IDataCheck, MergedSessionsCheck>();
        services.AddScoped<IDataCheck, OfficialClubPointsMismatchCheck>();
        services.AddScoped<IDataCheck, NoShowUnmatchedCheck>();
        services.AddScoped<IDataCheck, PlaceWithoutTimeCheck>();
        services.AddScoped<ILogligEventPullService, LogligEventPullService>();
        services.AddScoped<ILogligRelayBandService, LogligRelayBandService>();
        services.AddScoped<IDataCheck, SwimmerDedupCheck>();
        services.AddScoped<IDataCheck, ClubDedupCheck>();
        services.AddScoped<IDataCheck, SwimmerOrphanCheck>();
        services.AddScoped<IDataCheck, EmptyClubCheck>();
        services.AddScoped<IDataCheck, MergedClubStillUsedCheck>();
        services.AddScoped<IDataCheck, SwimmerTwoClubsInCompetitionCheck>();
        services.AddScoped<IDataCheck, SwimmerEnglishNameCheck>();
        services.AddScoped<IDataCheck, CompetitionWithoutClubPointRuleCheck>();
        services.AddScoped<IDataCheck, CountryDuplicateCheck>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();

        // Открытие для поиска (A10): заголовки страниц, sitemap.xml, «скрыть пловца из поиска».
        services.AddScoped<ISeoRepository, SeoRepository>();
        services.AddScoped<ISearchVisibilityService, SearchVisibilityService>();

        // Локальный вход (email + пароль)
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();

        // Email: есть Email:Smtp:Host (обычно через env Email__Smtp__* или user-secrets) → реальный
        // SMTP; нет — дев-реализация, логирующая письмо со ссылкой в консоль.
        var smtpSection = configuration.GetSection(SmtpEmailOptions.SectionName);
        if (!string.IsNullOrWhiteSpace(smtpSection["Host"]))
        {
            // Ручная привязка: не тащим Options.ConfigurationExtensions ради одной секции.
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new SmtpEmailOptions
            {
                Host = smtpSection["Host"]!.Trim(),
                Port = int.TryParse(smtpSection["Port"], out var smtpPort) ? smtpPort : 587,
                EnableSsl = !bool.TryParse(smtpSection["EnableSsl"], out var smtpSsl) || smtpSsl,
                User = smtpSection["User"] ?? "",
                Password = smtpSection["Password"] ?? "",
                From = smtpSection["From"] ?? "",
                FromName = smtpSection["FromName"] ?? "SwimHub",
            }));
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            // Тело письма (с одноразовым токеном) уходит в лог ТОЛЬКО в Development.
            // IHostEnvironment резолвим необязательно: AddInfrastructure должен оставаться
            // самодостаточным и вне хоста (в тестах его в контейнере нет — тогда false).
            services.AddSingleton<IEmailSender>(sp => new LoggingEmailSender(
                sp.GetRequiredService<ILogger<LoggingEmailSender>>(),
                logBody: sp.GetService<IHostEnvironment>()?.IsDevelopment() ?? false));
        }

        services.AddScoped<ILocalAuthService, LocalAuthService>();

        // DB migrator
        services.AddScoped<IDbMigrator, DbMigrator>();

        // Одноразовый сид рекордов/нормативов из легаси JS (dotnet run -- --seed-records <dir>)
        services.AddScoped<IRecordsSeeder, RecordsSeeder>();

        // Одноразовый сид тренировок «Дельфин-мастерс» (dotnet run -- --seed-dolphin-training ...)
        services.AddScoped<IDolphinTrainingSeeder, DolphinTrainingSeeder>();
        services.AddScoped<IPersonaSeeder, PersonaSeeder>();

        // Склейка пловцов-дублей (dry-run по умолчанию; см. docs/tasks/dedup-report.md)
        services.AddScoped<ISwimmerMergeService, SwimmerMergeService>();
        services.AddScoped<IRelayMemberBackfillService, RelayMemberBackfillService>();
        services.AddScoped<ISplitAttachService, SplitAttachService>();
        services.AddScoped<IRelayLegCleanupService, RelayLegCleanupService>();
        services.AddScoped<ISwimmerDedupService, SwimmerDedupService>();

        // Склейка клубов-дублей (docs/tasks/club-merge-plan.md, фаза B)
        services.AddScoped<IClubMergeService, ClubMergeService>();
        services.AddScoped<IClubDedupService, ClubDedupService>();

        // «Развязка» пар дедупа (пловцы/клубы): пара помечена «не дубли» — не всплывает
        services.AddScoped<IDedupIgnoreService, DedupIgnoreService>();

        // «Входящие» автозабора isr.org.il (фаза 6); провайдер живёт в Swimm.Parsing
        services.AddScoped<ICompetitionDiscoveryService, CompetitionDiscoveryService>();
        services.AddScoped<IStartListPullService, StartListPullService>();
        services.AddScoped<IStartListStitchService, StartListStitchService>();
        services.AddScoped<IStartListScheduleService, StartListScheduleService>();
        // Справка о старте: разминка руками + переопределение флага «чемпионат» (Т1)
        services.AddScoped<IMeetInfoAdminService, MeetInfoAdminService>();

        // «Затянуть» одну строку входящих — общий путь одиночной кнопки и пакетного забора
        services.AddScoped<IDiscoveryPreviewService, DiscoveryPreviewService>();

        // Пакетное затягивание: пачка живёт дольше HTTP-запроса, поэтому синглтон
        services.AddSingleton<IBulkPullService, BulkPullService>();

        // Ленивая сверка подозрительных заплывов превью с карточками loglig
        services.AddScoped<IPreviewRecordCheckService, PreviewRecordCheckService>();

        // Штамповка loglig-id пловцам по протоколу соревнования (после импорта)
        services.AddScoped<ILogligStampService, LogligStampService>();

        // Привязка соревнования к его compID-источникам стартового протокола (админка).
        services.AddScoped<ICompetitionSourceAdminService, CompetitionSourceAdminService>();

        // «Синхронизация языков» из Discovery: EN/HE-имена пловцов из двуязычной пары PDF
        services.AddScoped<ISwimmerNameSyncService, SwimmerNameSyncService>();

        // Чтение приватных тренировок группы (вкладка «Тренировки»)
        services.AddScoped<IHubGroupTrainingRepository, HubGroupTrainingRepository>();

        // Медиа группы (галерея + медиа тренировок)
        services.AddScoped<IHubGroupMediaService, HubGroupMediaService>();

        // Loglig ID (docs/loglig-id-plan.md, шаги 2-3): клиент карточки игрока + сверка результатов.
        // Карточка отдаётся после self-redirect с кукой DetailsPageVisited — нужны CookieContainer
        // и AllowAutoRedirect.
        services.AddHttpClient("loglig")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = new CookieContainer(),
                AllowAutoRedirect = true,
            });
        // Английские карточки: куку языка шлём заголовком, контейнера нет — иначе она
        // переключила бы язык и у общего «loglig» (LogligClient.GetPlayerEnglishNameAsync).
        services.AddHttpClient(LogligClient.EnglishClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                UseCookies = false,
                AllowAutoRedirect = true,
            });
        services.AddScoped<ILogligClient, LogligClient>();
        services.AddScoped<ILogligMatchService, LogligMatchService>();
        services.AddScoped<IOfficialClubStandingService, OfficialClubStandingService>();
        services.AddScoped<IRegulationFetchService, RegulationFetchService>();

        // Поиск кандидатов Loglig ID (шаг 4): serper.dev вместо закрытого Google CSE. Graceful —
        // пустой CandidateSearch:ApiKey отключает поиск (см. SerperCandidateSearchProvider).
        services.AddHttpClient("serper");
        services.AddScoped<ICandidateSearchProvider, SerperCandidateSearchProvider>();

        // Loglig ID (шаг 5): оркестрация привязки для админ-UI.
        services.AddScoped<ILogligLinkService, LogligLinkService>();

        // Loglig ID (шаг 6): краудсорс-предложения + ночная верификация.
        services.AddScoped<ILogligSuggestionService, LogligSuggestionService>();
        services.AddScoped<IEnglishNameRepairService, EnglishNameRepairService>();

        // Сводка «Статус данных» для дашборда /Admin (docs/plans/admin-dashboard-status-cards-plan.md)
        services.AddScoped<IRecordQualityService, RecordQualityService>();
        services.AddScoped<IDashboardStatusService, DashboardStatusService>();

        // Здоровье ссылок UserMedia (фаза 7.5): on-demand проверка живости по кнопке /Admin/Media.
        services.AddHttpClient("media-link-check", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(10);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("SwimmLinkCheck/1.0");
        });
        services.AddScoped<IUserMediaLinkChecker, UserMediaLinkChecker>();

        // Аудит ручных мутаций админки (фаза 7.4): запись «кто/что/когда» + чтение журнала.
        // Actor приходит через ICurrentActor (реализация в API-слое поверх HttpContext).
        services.AddScoped<IAdminAuditService, AdminAuditService>();
        services.AddScoped<IAdminAuditRepository, AdminAuditRepository>();

        // Deep-link выборки «здоровье данных» дашборда (T3b, docs/tasks/dashboard-deeplinks-lists-sonnet.md)
        services.AddScoped<IDataQualityService, DataQualityService>();
        services.AddScoped<ISuspectResultService, SuspectResultService>();

        return services;
    }

    /// <summary>
    /// Источник соединений моста кэша — свой, мимо пула EF: слушающее соединение живёт часами, и
    /// держать его в общем пуле значило бы отнять слот у запросов. KeepAlive — чтобы обрыв
    /// (перезапуск сервера, балансировщик Azure) замечался, а не висел молча.
    /// </summary>
    private static Npgsql.NpgsqlDataSource BridgeDataSource(string? connectionString, int timeoutSeconds)
    {
        var b = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = timeoutSeconds,
            CommandTimeout = timeoutSeconds,
            KeepAlive = 30,
            ApplicationName = "swimm-cache-bridge",
        };
        return Npgsql.NpgsqlDataSource.Create(b.ConnectionString);
    }
}
