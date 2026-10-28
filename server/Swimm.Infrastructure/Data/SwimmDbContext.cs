using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data;

public class SwimmDbContext : DbContext
{
    public SwimmDbContext(DbContextOptions<SwimmDbContext> options) : base(options)
    {
        // Сброс кэша по строкам верит старым значениям FK только у строк из запроса — следить
        // за подключёнными заглушками надо с рождения контекста (К4б.2).
        CacheInvalidationInterceptor.Watch(this, options);
    }

    /// <summary>
    /// Для наследников (напр. <see cref="SwimmReadDbContext"/>), которым нужна та же модель,
    /// но собственный тип DbContextOptions и отдельная строка подключения (другая DB-роль).
    /// </summary>
    protected SwimmDbContext(DbContextOptions options) : base(options)
    {
        CacheInvalidationInterceptor.Watch(this, options);
    }

    /* === Справочники === */
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<CompetitionEvent> CompetitionEvents => Set<CompetitionEvent>();
    public DbSet<CompetitionResultUrl> CompetitionResultUrls => Set<CompetitionResultUrl>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Swimmer> Swimmers => Set<Swimmer>();
    public DbSet<Relay> Relays => Set<Relay>();
    public DbSet<RelayMember> RelayMembers => Set<RelayMember>();
    public DbSet<Gallery> Galleries => Set<Gallery>();
    public DbSet<GalleryItem> GalleryItems => Set<GalleryItem>();
    public DbSet<Style> Styles => Set<Style>();
    public DbSet<Country> Countries => Set<Country>();

    /* === Категории соревнований === */
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryCompetition> CategoryCompetitions => Set<CategoryCompetition>();

    /* === Правила очков (клубные + пловца, Э0) === */
    public DbSet<PointRuleClubs> PointRulesClubs => Set<PointRuleClubs>();
    public DbSet<PointRuleClubsEntry> PointRulesClubsEntries => Set<PointRuleClubsEntry>();
    public DbSet<PointRuleSwimmers> PointRulesSwimmers => Set<PointRuleSwimmers>();
    public DbSet<PointRuleSwimmersEntry> PointRulesSwimmersEntries => Set<PointRuleSwimmersEntry>();

    /* === Серии турниров и официальное награждение (docs/plans/official-awards-plan.md) === */
    public DbSet<CompetitionSeries> CompetitionSeries => Set<CompetitionSeries>();
    public DbSet<CompetitionSeriesPart> CompetitionSeriesParts => Set<CompetitionSeriesPart>();
    public DbSet<SeriesRegulation> SeriesRegulations => Set<SeriesRegulation>();
    public DbSet<SeriesAward> SeriesAwards => Set<SeriesAward>();
    public DbSet<SeriesAwardBand> SeriesAwardBands => Set<SeriesAwardBand>();
    public DbSet<SeriesAwardPrize> SeriesAwardPrizes => Set<SeriesAwardPrize>();

    /* === Результаты === */
    public DbSet<ResultRecord> Results => Set<ResultRecord>();

    /* === Стартовый протокол: заявки (публично) + журнал заборов (Sys_) === */
    public DbSet<CompetitionEntry> CompetitionEntries => Set<CompetitionEntry>();
    public DbSet<StartListPull> StartListPulls => Set<StartListPull>();
    /// <summary>Из каких compID федерации состоит стартовый протокол соревнования (их бывает
    /// несколько на один старт — окружные протоколы, см. <see cref="CompetitionSource"/>).</summary>
    public DbSet<CompetitionSource> CompetitionSources => Set<CompetitionSource>();
    /// <summary>Справка о ПРЕДСТОЯЩЕМ старте: чемпионат ли и во сколько разминка (Т1).</summary>
    public DbSet<CompetitionMeetInfo> CompetitionMeetInfos => Set<CompetitionMeetInfo>();
    public DbSet<CompetitionWarmUp> CompetitionWarmUps => Set<CompetitionWarmUp>();

    /* === Клубный зачёт соревнования (материализованный; страница клуба, Фаза 10) === */
    public DbSet<ClubCompetitionStanding> ClubCompetitionStandings => Set<ClubCompetitionStanding>();
    public DbSet<CompetitionNote> CompetitionNotes => Set<CompetitionNote>();
    public DbSet<CompetitionNoteText> CompetitionNoteTexts => Set<CompetitionNoteText>();

    /* === Рекорды и нормативы (фаза 2) === */
    public DbSet<Record> Records => Set<Record>();
    public DbSet<NormativeStandard> NormativeStandards => Set<NormativeStandard>();

    /* === Качество рекордов (Sys_): реестр спорных записей + сверка с протоколами === */
    public DbSet<RecordIssue> RecordIssues => Set<RecordIssue>();
    public DbSet<RecordVerification> RecordVerifications => Set<RecordVerification>();
    public DbSet<RecordSourceCheck> RecordSourceChecks => Set<RecordSourceCheck>();

    /// <summary>Отладочные подробности витрин (двухуровневый выключатель, см. DebugOption).</summary>
    public DbSet<DebugOption> DebugOptions => Set<DebugOption>();
    /// <summary>Изменённые настройки /Admin/Settings (В7): реестр и дефолты — в AdminSettingsService.</summary>
    public DbSet<AdminSettingValue> AdminSettingValues => Set<AdminSettingValue>();

    /* === Импорт === */
    public DbSet<ImportHistory> ImportHistory => Set<ImportHistory>();
    public DbSet<ImportReconciliation> ImportReconciliations => Set<ImportReconciliation>();
    public DbSet<DataCheckRun> DataCheckRuns => Set<DataCheckRun>();
    public DbSet<DataCheckFinding> DataCheckFindings => Set<DataCheckFinding>();
    public DbSet<DataCheckState> DataCheckStates => Set<DataCheckState>();
    public DbSet<DiscoveredCompetition> DiscoveredCompetitions => Set<DiscoveredCompetition>();
    /// <summary>Журнал запусков фоновых задач по расписанию (В12): «последний запуск» переживает рестарт.</summary>
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    /// <summary>История прогонов фоновых задач с итогом (/Admin/Jobs); последние N на задачу.</summary>
    public DbSet<JobRunLogEntry> JobRunLog => Set<JobRunLogEntry>();

    /* === Фавориты и медиа пользователей === */
    public DbSet<UserFavorite> UserFavorites => Set<UserFavorite>();
    /// <summary>Персональный план на соревнование в табе Start list (Т3): за кем следит
    /// пользователь. Приватная таблица — плана нет в публичном read-пути.</summary>
    public DbSet<UserStartListPlan> UserStartListPlans => Set<UserStartListPlan>();
    public DbSet<UserMedia> UserMedia => Set<UserMedia>();
    public DbSet<UserMediaPublication> UserMediaPublications => Set<UserMediaPublication>();

    /* === Напоминания о тренировках через Web Push (docs/plans/web-push-reminders-plan.md) === */
    /// <summary>Push-подписки браузеров. Привязаны к устройству — в seed и в синхронизацию не едут.</summary>
    public DbSet<UserPushSubscription> UserPushSubscriptions => Set<UserPushSubscription>();
    public DbSet<UserNotificationSettings> UserNotificationSettings => Set<UserNotificationSettings>();
    public DbSet<HubGroupReminderMute> HubGroupReminderMutes => Set<HubGroupReminderMute>();
    /// <summary>Журнал доставки напоминаний; UNIQUE по занятию — защита от двойной отправки.</summary>
    public DbSet<ReminderDelivery> ReminderDeliveries => Set<ReminderDelivery>();

    /* === Реакции (лайки на медиа, поздравления на заплывы) === */
    public DbSet<UserReaction> UserReactions => Set<UserReaction>();
    public DbSet<MediaReport> MediaReports => Set<MediaReport>();

    /* === Группы (SwimHub) === */
    public DbSet<HubGroup> HubGroups => Set<HubGroup>();
    public DbSet<HubGroupMember> HubGroupMembers => Set<HubGroupMember>();
    /// <summary>Подписка группы на клуб: состав пересобирается из пловцов клуба. Публичная.</summary>
    public DbSet<HubGroupClubSubscription> HubGroupClubSubscriptions => Set<HubGroupClubSubscription>();
    public DbSet<HubGroupAdmin> HubGroupAdmins => Set<HubGroupAdmin>();
    public DbSet<HubGroupUserMember> HubGroupUserMembers => Set<HubGroupUserMember>();
    public DbSet<HubGroupClubRequest> HubGroupClubRequests => Set<HubGroupClubRequest>();
    public DbSet<HubGroupMedia> HubGroupMedia => Set<HubGroupMedia>();

    /* === Дедуп: «развязанные» пары (Sys_) === */
    public DbSet<DedupIgnoredPair> DedupIgnoredPairs => Set<DedupIgnoredPair>();

    /* === Аудит ручных мутаций админки (фаза 7.4, Sys_) === */
    public DbSet<AdminAudit> AdminAudits => Set<AdminAudit>();

    /* === Тренировки (приватные, Sys_) === */
    public DbSet<TrainingSession> TrainingSessions => Set<TrainingSession>();
    public DbSet<TrainingResult> TrainingResults => Set<TrainingResult>();

    /* === Уровни пловцов группы (приватные, Sys_; docs/plans/lane-plans-plan.md) === */
    public DbSet<HubGroupLevel> HubGroupLevels => Set<HubGroupLevel>();
    public DbSet<HubGroupSwimmerLevel> HubGroupSwimmerLevels => Set<HubGroupSwimmerLevel>();
    public DbSet<LanePlan> LanePlans => Set<LanePlan>();
    public DbSet<LanePlanLane> LanePlanLanes => Set<LanePlanLane>();
    public DbSet<LanePlanSwimmer> LanePlanSwimmers => Set<LanePlanSwimmer>();
    public DbSet<HubGroupTrainingRsvp> HubGroupTrainingRsvps => Set<HubGroupTrainingRsvp>();
    public DbSet<HubGroupBreak> HubGroupBreaks => Set<HubGroupBreak>();
    public DbSet<HubGroupTrainingCancellation> HubGroupTrainingCancellations => Set<HubGroupTrainingCancellation>();
    public DbSet<HubGroupAccountLevel> HubGroupAccountLevels => Set<HubGroupAccountLevel>();
    public DbSet<HubGroupUserSeen> HubGroupUserSeen => Set<HubGroupUserSeen>();

    /* === Пользователи и доступ === */
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AppRole> AppRoles => Set<AppRole>();
    public DbSet<AppUserRole> AppUserRoles => Set<AppUserRole>();
    public DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();
    public DbSet<UserLoginHistory> UserLoginHistory => Set<UserLoginHistory>();
    public DbSet<UserLocalCredential> UserLocalCredentials => Set<UserLocalCredential>();
    public DbSet<UserSecurityToken> UserSecurityTokens => Set<UserSecurityToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Конфигурация — по классу IEntityTypeConfiguration<T> на таблицу, файлы по темам в
        // Data/Configurations/. Новая таблица = новый класс там же, подхватится сам.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SwimmDbContext).Assembly);
    }
}
