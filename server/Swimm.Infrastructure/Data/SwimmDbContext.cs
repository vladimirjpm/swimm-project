using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data;

public class SwimmDbContext : DbContext
{
    public SwimmDbContext(DbContextOptions<SwimmDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Для наследников (напр. <see cref="SwimmReadDbContext"/>), которым нужна та же модель,
    /// но собственный тип DbContextOptions и отдельная строка подключения (другая DB-роль).
    /// </summary>
    protected SwimmDbContext(DbContextOptions options) : base(options)
    {
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

    /* === Клубные очки === */
    public DbSet<ClubPointsRule> ClubPointsRules => Set<ClubPointsRule>();
    public DbSet<ClubPointsRuleEntry> ClubPointsRuleEntries => Set<ClubPointsRuleEntry>();

    /* === Результаты === */
    public DbSet<ResultRecord> Results => Set<ResultRecord>();

    /* === Рекорды и нормативы (фаза 2) === */
    public DbSet<Record> Records => Set<Record>();
    public DbSet<NormativeStandard> NormativeStandards => Set<NormativeStandard>();

    /* === Импорт === */
    public DbSet<ImportHistory> ImportHistory => Set<ImportHistory>();
    public DbSet<DiscoveredCompetition> DiscoveredCompetitions => Set<DiscoveredCompetition>();

    /* === Фавориты и медиа пользователей === */
    public DbSet<UserFavorite> UserFavorites => Set<UserFavorite>();
    public DbSet<UserMedia> UserMedia => Set<UserMedia>();
    public DbSet<UserMediaPublication> UserMediaPublications => Set<UserMediaPublication>();

    /* === Реакции (лайки на медиа, поздравления на заплывы) === */
    public DbSet<UserReaction> UserReactions => Set<UserReaction>();

    /* === Группы (SwimHub) === */
    public DbSet<HubGroup> HubGroups => Set<HubGroup>();
    public DbSet<HubGroupMember> HubGroupMembers => Set<HubGroupMember>();
    public DbSet<HubGroupAdmin> HubGroupAdmins => Set<HubGroupAdmin>();
    public DbSet<HubGroupUserMember> HubGroupUserMembers => Set<HubGroupUserMember>();
    public DbSet<HubGroupClubRequest> HubGroupClubRequests => Set<HubGroupClubRequest>();
    public DbSet<HubGroupMedia> HubGroupMedia => Set<HubGroupMedia>();

    /* === Дедуп: «развязанные» пары (Sys_) === */
    public DbSet<DedupIgnoredPair> DedupIgnoredPairs => Set<DedupIgnoredPair>();

    /* === Тренировки (приватные, Sys_) === */
    public DbSet<TrainingSession> TrainingSessions => Set<TrainingSession>();
    public DbSet<TrainingResult> TrainingResults => Set<TrainingResult>();

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

        // --- Справочники ---

        modelBuilder.Entity<Competition>(entity =>
        {
            entity.ToTable("Competitions");
            entity.HasIndex(e => new { e.Name, e.Date, e.PoolType }).IsUnique();

            // Уникальность OrgCompId обеспечена реальным UNIQUE constraint'ом (raw SQL в миграции),
            // а не EF alternate key — тот требует NOT NULL, а OrgCompId должен быть nullable
            // (у старых соревнований его пока нет). См. CompetitionResultUrl.

            // Многодневные соревнования: день → событие. SetNull — удаление события не трогает дни.
            entity.HasOne(e => e.Event)
                .WithMany(ev => ev.Days)
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CompetitionEvent>(entity =>
        {
            entity.ToTable("CompetitionEvents");
        });

        modelBuilder.Entity<CompetitionResultUrl>(entity =>
        {
            entity.ToTable("CompetitionResultUrls");
            entity.HasIndex(e => new { e.OrgCompId, e.Culture }).IsUnique();

            // Связь с Competition идёт по OrgCompId, не Id — намеренно не смоделирована как EF
            // relationship (см. комментарий у Competition.OrgCompId). FK создан raw SQL в миграции.
        });

        modelBuilder.Entity<Club>(entity =>
        {
            entity.ToTable("Clubs");

            entity.HasOne(e => e.Country)
                .WithMany()
                .HasForeignKey(e => e.CountryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Swimmer>(entity =>
        {
            entity.ToTable("Swimmers");

            // Существующие/новые isr-пловцы = 'isr' по умолчанию; local проставляет сидер явно.
            entity.Property(e => e.Origin).HasDefaultValue("isr");

            entity.HasOne(e => e.Club)
                .WithMany()
                .HasForeignKey(e => e.ClubId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Country)
                .WithMany()
                .HasForeignKey(e => e.CountryId)
                .OnDelete(DeleteBehavior.SetNull);

            // Привязка к loglig.com уникальна только среди заполненных значений — частичный
            // индекс (Npgsql по умолчанию делает индекс по nullable-колонке полным, не partial).
            entity.HasIndex(e => e.LogligId)
                .IsUnique()
                .HasFilter("\"LogligId\" IS NOT NULL");
        });

        modelBuilder.Entity<Country>(entity =>
        {
            entity.ToTable("Countries");
        });

        modelBuilder.Entity<Relay>(entity =>
        {
            entity.ToTable("Relays");
        });

        // Ноги эстафеты (публичная reference-таблица, грант swimm_ro в миграции) —
        // структурное членство «эстафета → пловцы», см. RelayMember.
        modelBuilder.Entity<RelayMember>(entity =>
        {
            entity.ToTable("RelayMembers");

            entity.HasOne(e => e.Relay)
                .WithMany(r => r.Members)
                .HasForeignKey(e => e.RelayId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Swimmer)
                .WithMany()
                .HasForeignKey(e => e.SwimmerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Style>(entity =>
        {
            entity.ToTable("Styles");
            entity.HasData(
                new Style { Id = 1, Name = "freestyle" },
                new Style { Id = 2, Name = "backstroke" },
                new Style { Id = 3, Name = "breaststroke" },
                new Style { Id = 4, Name = "butterfly" },
                new Style { Id = 5, Name = "individual_medley" },
                new Style { Id = 6, Name = "medley_relay" },
                new Style { Id = 7, Name = "free_relay" }
            );
        });

        // --- Категории соревнований ---

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasIndex(e => e.Key).IsUnique();

            entity.HasData(
                new Category { Id = 1, Key = "results-main",       Name = "Main Results", DisplayOrder = 1 },
                new Category { Id = 2, Key = "results-masters",    Name = "Masters",      DisplayOrder = 2, Badge = "M" },
                new Category { Id = 3, Key = "results-youth-team",    Name = "Youth Results",  DisplayOrder = 3, Badge = "Y" },
                new Category { Id = 4, Key = "results-junior-results", Name = "Junior Results", DisplayOrder = 4, Badge = "J" }
            );
        });

        modelBuilder.Entity<CategoryCompetition>(entity =>
        {
            entity.ToTable("CategoryCompetitions");
            entity.HasIndex(e => new { e.CategoryId, e.CompetitionId }).IsUnique();

            entity.HasOne(e => e.Category)
                .WithMany(c => c.Competitions)
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Competition)
                .WithMany()
                .HasForeignKey(e => e.CompetitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // --- Рекорды и нормативы ---

        modelBuilder.Entity<Record>(entity =>
        {
            entity.ToTable("Records");
            // 8 осей = уникальная позиция рекорда; RegionCode/AgeKey — NOT NULL (пустая строка),
            // иначе Postgres NULLS DISTINCT пропустил бы дубли.
            entity.HasIndex(e => new
            {
                e.RegionType, e.RegionCode, e.Category, e.AgeKey,
                e.Gender, e.PoolType, e.Style, e.Distance
            }).IsUnique();
            // Горячий путь клиента/кэша — выборка региона (± категории).
            entity.HasIndex(e => new { e.RegionType, e.RegionCode, e.Category });
        });

        modelBuilder.Entity<NormativeStandard>(entity =>
        {
            entity.ToTable("NormativeStandards");
            entity.HasIndex(e => new
            {
                e.Kind, e.Country, e.Gender, e.PoolType,
                e.Style, e.Distance, e.AgeKey, e.Level
            }).IsUnique();
        });

        // --- Правила клубных очков ---

        modelBuilder.Entity<ClubPointsRule>(entity =>
        {
            entity.ToTable("ClubPointsRules");
            entity.HasIndex(e => e.Version).IsUnique();

            entity.HasMany(e => e.Entries)
                .WithOne(e => e.Rule)
                .HasForeignKey(e => e.RuleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasData(
                new ClubPointsRule
                {
                    Id = 1,
                    Version = "2025.01",
                    EffectiveFrom = new DateOnly(2025, 1, 1),
                    Description = "Israeli swimming club points system starting Jan 2025",
                    Scope = "all",
                    DefaultPoints = 0,
                    MaxScoringPlace = 24
                },
                new ClubPointsRule
                {
                    Id = 2,
                    Version = "2025.01-masters",
                    EffectiveFrom = new DateOnly(2025, 1, 1),
                    Description = "Masters club points system (places 1-12)",
                    Scope = "masters",
                    DefaultPoints = 0,
                    MaxScoringPlace = 12
                }
            );
        });

        modelBuilder.Entity<ClubPointsRuleEntry>(entity =>
        {
            entity.ToTable("ClubPointsRuleEntries");
            entity.HasIndex(e => new { e.RuleId, e.Place }).IsUnique();

            // Seed: правило 1 — шкала 24 мест (israelская система)
            entity.HasData(
                new ClubPointsRuleEntry { Id =  1, RuleId = 1, Place =  1, Points = 30 },
                new ClubPointsRuleEntry { Id =  2, RuleId = 1, Place =  2, Points = 28 },
                new ClubPointsRuleEntry { Id =  3, RuleId = 1, Place =  3, Points = 26 },
                new ClubPointsRuleEntry { Id =  4, RuleId = 1, Place =  4, Points = 24 },
                new ClubPointsRuleEntry { Id =  5, RuleId = 1, Place =  5, Points = 23 },
                new ClubPointsRuleEntry { Id =  6, RuleId = 1, Place =  6, Points = 22 },
                new ClubPointsRuleEntry { Id =  7, RuleId = 1, Place =  7, Points = 21 },
                new ClubPointsRuleEntry { Id =  8, RuleId = 1, Place =  8, Points = 20 },
                new ClubPointsRuleEntry { Id =  9, RuleId = 1, Place =  9, Points = 19 },
                new ClubPointsRuleEntry { Id = 10, RuleId = 1, Place = 10, Points = 18 },
                new ClubPointsRuleEntry { Id = 11, RuleId = 1, Place = 11, Points = 16 },
                new ClubPointsRuleEntry { Id = 12, RuleId = 1, Place = 12, Points = 15 },
                new ClubPointsRuleEntry { Id = 13, RuleId = 1, Place = 13, Points = 14 },
                new ClubPointsRuleEntry { Id = 14, RuleId = 1, Place = 14, Points = 13 },
                new ClubPointsRuleEntry { Id = 15, RuleId = 1, Place = 15, Points = 12 },
                new ClubPointsRuleEntry { Id = 16, RuleId = 1, Place = 16, Points = 11 },
                new ClubPointsRuleEntry { Id = 17, RuleId = 1, Place = 17, Points = 10 },
                new ClubPointsRuleEntry { Id = 18, RuleId = 1, Place = 18, Points =  9 },
                new ClubPointsRuleEntry { Id = 19, RuleId = 1, Place = 19, Points =  8 },
                new ClubPointsRuleEntry { Id = 20, RuleId = 1, Place = 20, Points =  7 },
                new ClubPointsRuleEntry { Id = 21, RuleId = 1, Place = 21, Points =  5 },
                new ClubPointsRuleEntry { Id = 22, RuleId = 1, Place = 22, Points =  3 },
                new ClubPointsRuleEntry { Id = 23, RuleId = 1, Place = 23, Points =  2 },
                new ClubPointsRuleEntry { Id = 24, RuleId = 1, Place = 24, Points =  1 },
                // Seed: правило 2 — Masters, шкала 12 мест
                new ClubPointsRuleEntry { Id = 25, RuleId = 2, Place =  1, Points = 12 },
                new ClubPointsRuleEntry { Id = 26, RuleId = 2, Place =  2, Points = 11 },
                new ClubPointsRuleEntry { Id = 27, RuleId = 2, Place =  3, Points = 10 },
                new ClubPointsRuleEntry { Id = 28, RuleId = 2, Place =  4, Points =  9 },
                new ClubPointsRuleEntry { Id = 29, RuleId = 2, Place =  5, Points =  8 },
                new ClubPointsRuleEntry { Id = 30, RuleId = 2, Place =  6, Points =  7 },
                new ClubPointsRuleEntry { Id = 31, RuleId = 2, Place =  7, Points =  6 },
                new ClubPointsRuleEntry { Id = 32, RuleId = 2, Place =  8, Points =  5 },
                new ClubPointsRuleEntry { Id = 33, RuleId = 2, Place =  9, Points =  4 },
                new ClubPointsRuleEntry { Id = 34, RuleId = 2, Place = 10, Points =  3 },
                new ClubPointsRuleEntry { Id = 35, RuleId = 2, Place = 11, Points =  2 },
                new ClubPointsRuleEntry { Id = 36, RuleId = 2, Place = 12, Points =  1 }
            );
        });

        // --- Таблица результатов ---

        modelBuilder.Entity<ResultRecord>(entity =>
        {
            entity.ToTable("Results");

            // CompetitionDate — календарная дата соревнования, а не момент времени с таймзоной.
            // Маппим в timestamp without time zone: значения приходят из парсинга с Kind=Unspecified,
            // и timestamptz (требующий Kind=Utc) на них падал бы при записи/фильтрации.
            entity.Property(r => r.CompetitionDate)
                .HasColumnType("timestamp without time zone");

            entity.HasOne(r => r.Competition)
                .WithMany()
                .HasForeignKey(r => r.CompetitionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.Swimmer)
                .WithMany()
                .HasForeignKey(r => r.SwimmerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.Club)
                .WithMany()
                .HasForeignKey(r => r.ClubId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.Style)
                .WithMany()
                .HasForeignKey(r => r.StyleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.Relay)
                .WithMany()
                .HasForeignKey(r => r.RelayId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(r => r.Gallery)
                .WithMany()
                .HasForeignKey(r => r.GalleryId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(r => r.Country)
                .WithMany()
                .HasForeignKey(r => r.CountryId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasCheckConstraint("CK_Results_Heat_NonNegative", "\"Heat\" >= 0");
            entity.HasCheckConstraint("CK_Results_Lane_NonNegative", "\"Lane\" >= 0");
            entity.HasCheckConstraint("CK_Results_InternationalPoints_NonNegative", "\"InternationalPoints\" >= 0");
            entity.HasCheckConstraint("CK_Results_Position_PositiveOrNull", "\"Position\" IS NULL OR \"Position\" > 0");
            entity.HasCheckConstraint("CK_Results_PositionAgeGroup_PositiveOrNull", "\"PositionAgeGroup\" IS NULL OR \"PositionAgeGroup\" > 0");
        });

        // --- Импорт ---

        // «Входящие» автозабора isr.org.il (фаза 6) — приватная Sys_-таблица, без grant swimm_ro.
        modelBuilder.Entity<DiscoveredCompetition>(entity =>
        {
            entity.ToTable("Sys_DiscoveredCompetitions");
        });

        modelBuilder.Entity<ImportHistory>(entity =>
        {
            entity.ToTable("Sys_ImportHistory");
            entity.HasIndex(e => e.CompetitionId);
            entity.HasIndex(e => e.ImportDate);

            entity.HasOne(e => e.Competition)
                .WithMany()
                .HasForeignKey(e => e.CompetitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // --- Пользователи и доступ ---

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Sys_AppUsers");
            entity.HasIndex(e => e.Email).IsUnique();

            entity.HasOne(e => e.Swimmer)
                .WithMany()
                .HasForeignKey(e => e.SwimmerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AppRole>(entity =>
        {
            entity.ToTable("Sys_AppRoles");
            entity.HasIndex(e => e.Name).IsUnique();

            entity.HasData(
                new AppRole { Id = 1, Name = "Admin" },
                new AppRole { Id = 2, Name = "User" },
                new AppRole { Id = 3, Name = "Coach" }
            );
        });

        modelBuilder.Entity<AppUserRole>(entity =>
        {
            entity.ToTable("Sys_AppUserRoles");
            entity.HasKey(e => new { e.UserId, e.RoleId });

            entity.HasOne(e => e.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Role)
                .WithMany()
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserExternalLogin>(entity =>
        {
            entity.ToTable("Sys_UserExternalLogins");
            entity.HasIndex(e => new { e.Provider, e.ProviderKey }).IsUnique();

            entity.HasOne(e => e.User)
                .WithMany(u => u.ExternalLogins)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

        });

        // Локальные учётные данные (email+пароль) — связь 1:1 с AppUser по общему PK.
        // Таблица заведена заранее; логика локального входа пока не реализована.
        modelBuilder.Entity<UserLocalCredential>(entity =>
        {
            entity.ToTable("Sys_UserLocalCredentials");
            entity.HasKey(e => e.UserId);

            entity.HasOne(e => e.User)
                .WithOne(u => u.LocalCredential)
                .HasForeignKey<UserLocalCredential>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserLoginHistory>(entity =>
        {
            entity.ToTable("Sys_UserLoginHistory");
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.LoginAt);
            // Существующие строки истории — успешные логины (фейлы начали писаться позже).
            entity.Property(e => e.Success).HasDefaultValue(true);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Одноразовые токены (email-verify / password-reset). Храним только хеш токена.
        modelBuilder.Entity<UserSecurityToken>(entity =>
        {
            entity.ToTable("Sys_UserSecurityTokens");
            entity.HasIndex(e => e.TokenHash);
            entity.HasIndex(e => new { e.UserId, e.Purpose });

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // --- Фавориты пользователей ---

        modelBuilder.Entity<UserFavorite>(entity =>
        {
            entity.ToTable("Sys_UserFavorites");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Swimmer)
                .WithMany()
                .HasForeignKey(e => e.SwimmerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Club)
                .WithMany()
                .HasForeignKey(e => e.ClubId)
                .OnDelete(DeleteBehavior.Restrict);

            // Дискриминатор: swimmer → SwimmerId NOT NULL, ClubId NULL; club → обратно.
            entity.HasCheckConstraint(
                "CK_UserFav_TargetType",
                @"(""TargetType"" = 'swimmer' AND ""SwimmerId"" IS NOT NULL AND ""ClubId"" IS NULL) OR " +
                @"(""TargetType"" = 'club'    AND ""ClubId""    IS NOT NULL AND ""SwimmerId"" IS NULL)");

            // Partial unique indexes создаются вручную в миграции через migrationBuilder.Sql
            // (UX_UserFav_OnePrimary, UX_UserFav_Swimmer, UX_UserFav_Club).
        });

        // --- Медиа пользователей (Phase 2; таблица создана в Phase 1, эндпоинты — нет) ---

        modelBuilder.Entity<UserMedia>(entity =>
        {
            entity.ToTable("Sys_UserMedia");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Swimmer)
                .WithMany()
                .HasForeignKey(e => e.SwimmerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ResultRecord)
                .WithMany()
                .HasForeignKey(e => e.ResultId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Competition)
                .WithMany()
                .HasForeignKey(e => e.CompetitionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasCheckConstraint(
                "CK_UserMedia_Level",
                @"""Level"" IN ('swimmer', 'competition', 'result')");

            entity.HasCheckConstraint(
                "CK_UserMedia_Visibility",
                @"""Visibility"" IN ('private', 'public')");
        });

        // Публикации личного медиа в группы (этап 2 media-visibility-model) — заявки/решения,
        // таблица с личными данными, БЕЗ grant swimm_ro (читается сервисами через RW-контекст).
        modelBuilder.Entity<UserMediaPublication>(entity =>
        {
            entity.ToTable("Sys_UserMediaPublications");

            entity.HasOne(e => e.Media)
                .WithMany()
                .HasForeignKey(e => e.UserMediaId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.HubGroup)
                .WithMany()
                .HasForeignKey(e => e.HubGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.DecidedBy)
                .WithMany()
                .HasForeignKey(e => e.DecidedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasCheckConstraint(
                "CK_UserMediaPublications_Level",
                @"""Level"" IN ('members', 'public')");

            entity.HasCheckConstraint(
                "CK_UserMediaPublications_Status",
                @"""Status"" IN ('pending', 'approved', 'rejected')");
        });

        // Реакции пользователей (❤ на медиа / 🎉 на заплыв) — Sys_-таблица БЕЗ grant swimm_ro:
        // публичные счётчики отдаются агрегатами через RW-контекст, сырые строки наружу не идут.
        modelBuilder.Entity<UserReaction>(entity =>
        {
            entity.ToTable("Sys_UserReactions");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Media)
                .WithMany()
                .HasForeignKey(e => e.MediaId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ResultRecord)
                .WithMany()
                .HasForeignKey(e => e.ResultId)
                .OnDelete(DeleteBehavior.Cascade);

            // Дискриминатор: like → MediaId NOT NULL, congrats → ResultId NOT NULL.
            entity.HasCheckConstraint(
                "CK_UserReactions_Kind",
                @"(""Kind"" = 'like'     AND ""MediaId""  IS NOT NULL AND ""ResultId"" IS NULL) OR " +
                @"(""Kind"" = 'congrats' AND ""ResultId"" IS NOT NULL AND ""MediaId""  IS NULL)");

            // Partial unique indexes (одна реакция на юзера+цель) + счётные индексы по цели —
            // вручную в миграции через migrationBuilder.Sql (UX_UserReactions_Like/Congrats).
        });

        // --- Группы (SwimHub) ---

        modelBuilder.Entity<HubGroup>(entity =>
        {
            entity.ToTable("HubGroups");

            entity.HasOne(e => e.Club)
                .WithMany()
                .HasForeignKey(e => e.ClubId)
                .OnDelete(DeleteBehavior.SetNull);

            // FK на Sys_AppUsers допустим (FK на уровне БД не требует SELECT-права у swimm_ro);
            // навигацию Owner не Include-ить нигде за пределами админки.
            entity.HasOne(e => e.Owner)
                .WithMany()
                .HasForeignKey(e => e.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Официальна СВЯЗЬ с клубом, не состав: одна официальная группа на клуб,
            // и она всегда привязана к клубу (фаза 8.7).
            entity.HasIndex(e => e.ClubId)
                .IsUnique()
                .HasFilter(@"""IsOfficial""")
                .HasDatabaseName("IX_HubGroups_ClubId_Official");

            entity.HasCheckConstraint(
                "CK_HubGroups_OfficialRequiresClub",
                @"NOT ""IsOfficial"" OR ""ClubId"" IS NOT NULL");

            entity.HasCheckConstraint(
                "CK_HubGroups_JoinPolicy",
                @"""JoinPolicy"" IN ('open', 'approval')");
        });

        modelBuilder.Entity<HubGroupMember>(entity =>
        {
            entity.ToTable("HubGroupMembers");

            entity.HasOne(e => e.HubGroup)
                .WithMany(g => g.Members)
                .HasForeignKey(e => e.HubGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Swimmer)
                .WithMany()
                .HasForeignKey(e => e.SwimmerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Админы группы (право правки) — таблица прав, БЕЗ grant swimm_ro.
        modelBuilder.Entity<HubGroupAdmin>(entity =>
        {
            entity.ToTable("Sys_HubGroupAdmins");
            entity.HasIndex(e => new { e.HubGroupId, e.UserId }).IsUnique();

            entity.HasOne(e => e.HubGroup)
                .WithMany(g => g.Admins)
                .HasForeignKey(e => e.HubGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.GrantedBy)
                .WithMany()
                .HasForeignKey(e => e.GrantedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Участники-аккаунты (приватный состав из юзеров) — таблица личных данных, БЕЗ grant swimm_ro.
        modelBuilder.Entity<HubGroupUserMember>(entity =>
        {
            entity.ToTable("Sys_HubGroupUserMembers");
            entity.HasIndex(e => new { e.HubGroupId, e.UserId }).IsUnique();

            entity.HasOne(e => e.HubGroup)
                .WithMany(g => g.UserMembers)
                .HasForeignKey(e => e.HubGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AddedBy)
                .WithMany()
                .HasForeignKey(e => e.AddedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Swimmer)
                .WithMany()
                .HasForeignKey(e => e.SwimmerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // «Развязанные» пары дедупа (админ подтвердил «не дубли») — Sys_-таблица, БЕЗ grant swimm_ro.
        modelBuilder.Entity<DedupIgnoredPair>(entity =>
        {
            entity.ToTable("Sys_DedupIgnoredPairs");
            entity.HasIndex(e => new { e.EntityType, e.IdA, e.IdB }).IsUnique();
        });

        // Заявки на официальный статус группы (право/личные данные) — Sys_-таблица, БЕЗ grant swimm_ro.
        modelBuilder.Entity<HubGroupClubRequest>(entity =>
        {
            entity.ToTable("Sys_HubGroupClubRequests");

            // Одна pending-заявка на группу одновременно.
            entity.HasIndex(e => e.HubGroupId)
                .IsUnique()
                .HasFilter(@"""Status"" = 'pending'")
                .HasDatabaseName("IX_Sys_HubGroupClubRequests_HubGroupId_Pending");

            entity.HasOne(e => e.HubGroup)
                .WithMany()
                .HasForeignKey(e => e.HubGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Club)
                .WithMany()
                .HasForeignKey(e => e.ClubId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.DecidedByUser)
                .WithMany()
                .HasForeignKey(e => e.DecidedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Медиа группы (только ссылки) — TrainingId null = публичная галерея, задан = медиа
        // тренировки (та же приватность, что у Sys_TrainingSessions). Sys_-таблица БЕЗ grant
        // swimm_ro: публичная галерея читается через SwimmDbContext, не read-реплику.
        modelBuilder.Entity<HubGroupMedia>(entity =>
        {
            entity.ToTable("Sys_HubGroupMedia");
            entity.HasIndex(e => e.HubGroupId);
            entity.HasIndex(e => e.TrainingId);

            entity.HasOne(e => e.HubGroup)
                .WithMany()
                .HasForeignKey(e => e.HubGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Training)
                .WithMany()
                .HasForeignKey(e => e.TrainingId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Якоря 2B′: удаление пловца/заплыва уносит и привязанные разборы (cascade) —
            // медиа-ссылка без своего якоря бессмысленна.
            entity.HasOne(e => e.Swimmer)
                .WithMany()
                .HasForeignKey(e => e.SwimmerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Result)
                .WithMany()
                .HasForeignKey(e => e.ResultId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasCheckConstraint(
                "CK_HubGroupMedia_MediaType",
                @"""MediaType"" IN ('image', 'video', 'album')");

            entity.HasCheckConstraint(
                "CK_HubGroupMedia_SourceType",
                @"""SourceType"" IN ('youtube', 'vimeo', 'album', 'other')");

            entity.HasCheckConstraint(
                "CK_HubGroupMedia_AlbumInvariant",
                @"(""MediaType"" = 'album') = (""SourceType"" = 'album')");

            entity.HasCheckConstraint(
                "CK_HubGroupMedia_Visibility",
                @"""Visibility"" IN ('public', 'members')");

            // Якорь на персону — только members-слой: публично вешать медиа на пловца нельзя.
            entity.HasCheckConstraint(
                "CK_HubGroupMedia_AnchorMembersOnly",
                @"(""SwimmerId"" IS NULL AND ""ResultId"" IS NULL) OR ""Visibility"" = 'members'");
        });

        // Тренировочная сессия — ПРИВАТНЫЕ данные группы, Sys_-таблица БЕЗ grant swimm_ro.
        modelBuilder.Entity<TrainingSession>(entity =>
        {
            entity.ToTable("Sys_TrainingSessions");

            entity.HasOne(e => e.HubGroup)
                .WithMany()
                .HasForeignKey(e => e.HubGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Тренировочный повтор — ПРИВАТНЫЕ данные, Sys_-таблица БЕЗ grant swimm_ro.
        // SwimmerId ссылается на публичную Swimmers (приватны времена, не сам пловец) → Restrict,
        // чтобы удаление пловца не сносило тренировки молча.
        modelBuilder.Entity<TrainingResult>(entity =>
        {
            entity.ToTable("Sys_TrainingResults");

            entity.HasOne(e => e.Session)
                .WithMany(s => s.Results)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Swimmer)
                .WithMany()
                .HasForeignKey(e => e.SwimmerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Style)
                .WithMany()
                .HasForeignKey(e => e.StyleId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
