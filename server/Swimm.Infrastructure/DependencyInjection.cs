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

        services.AddDbContext<SwimmDbContext>(options =>
            options.UseNpgsql(adminCs, npgsql =>
                npgsql.EnableRetryOnFailure(maxRetryCount: 3)));

        services.AddDbContext<SwimmReadDbContext>(options => options
            .UseNpgsql(readCs, npgsql =>
                npgsql.EnableRetryOnFailure(maxRetryCount: 3))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        // Кэш — IMemoryCache для одного инстанса; для Redis заменить MemoryCacheService
        // на RedisCacheService (IDistributedCache + JSON) без изменений в потребителях.
        services.AddMemoryCache();
        services.AddSingleton<ICacheService, MemoryCacheService>();

        // Settings (singleton — in-memory store)
        services.AddSingleton<ISettingsService, AdminSettingsService>();
        services.AddScoped<IDebugOptionsService, DebugOptionsService>();

        // Scoped services
        services.AddScoped<IImportService, JsonImportService>();
        services.AddScoped<ISchemaService, DbSchemaService>();

        // Import job queue — singleton, разделяется между контроллером и BackgroundService
        services.AddSingleton<ImportJobQueue>();
        services.AddSingleton<IImportJobQueue>(sp => sp.GetRequiredService<ImportJobQueue>());

        // Repositories
        services.AddScoped<IResultRepository, ResultRepository>();
        services.AddScoped<IRecordRepository, RecordRepository>();
        services.AddScoped<IRecordAdminRepository, RecordAdminRepository>();
        services.AddScoped<IRecordDiffService, RecordDiffService>();
        services.AddScoped<IAdminRepository, AdminRepository>();
        services.AddScoped<ICompetitionAdminRepository, CompetitionAdminRepository>();
        services.AddScoped<ICategoryAdminRepository, CategoryAdminRepository>();
        services.AddScoped<IStyleAdminRepository, StyleAdminRepository>();
        services.AddScoped<IPointRulesAdminRepository, PointRulesAdminRepository>();
        services.AddScoped<IResultAdminRepository, ResultAdminRepository>();
        services.AddScoped<IResultTransferService, ResultTransferService>();
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
        services.AddScoped<HubGroupCrudCore>();
        services.AddScoped<IHubGroupAdminService, HubGroupAdminService>();
        services.AddScoped<IHubGroupPublicRepository, HubGroupPublicRepository>();
        services.AddScoped<IHubGroupPermissionService, HubGroupPermissionService>();
        services.AddScoped<IHubGroupUserService, HubGroupUserService>();
        services.AddScoped<IHubGroupClubRequestAdminService, HubGroupClubRequestAdminService>();
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
        services.AddScoped<ILogligEventPullService, LogligEventPullService>();
        services.AddScoped<ILogligRelayBandService, LogligRelayBandService>();
        services.AddScoped<IDataCheck, SwimmerDedupCheck>();
        services.AddScoped<IDataCheck, ClubDedupCheck>();
        services.AddScoped<IDataCheck, SwimmerOrphanCheck>();
        services.AddScoped<IDataCheck, EmptyClubCheck>();
        services.AddScoped<IDataCheck, MergedClubStillUsedCheck>();
        services.AddScoped<IDataCheck, SwimmerTwoClubsInCompetitionCheck>();
        services.AddScoped<IDataCheck, CompetitionWithoutClubPointRuleCheck>();
        services.AddScoped<IDataCheck, CountryDuplicateCheck>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();

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

        // Склейка пловцов-дублей (dry-run по умолчанию; см. docs/tasks/dedup-report.md)
        services.AddScoped<ISwimmerMergeService, SwimmerMergeService>();
        services.AddScoped<IRelayMemberBackfillService, RelayMemberBackfillService>();
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
}
