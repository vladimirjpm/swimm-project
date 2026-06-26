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
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Swimmer> Swimmers => Set<Swimmer>();
    public DbSet<Relay> Relays => Set<Relay>();
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

    /* === Импорт === */
    public DbSet<ImportHistory> ImportHistory => Set<ImportHistory>();

    /* === Фавориты и медиа пользователей === */
    public DbSet<UserFavorite> UserFavorites => Set<UserFavorite>();
    public DbSet<UserMedia> UserMedia => Set<UserMedia>();

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

            entity.HasOne(e => e.Club)
                .WithMany()
                .HasForeignKey(e => e.ClubId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Country)
                .WithMany()
                .HasForeignKey(e => e.CountryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Country>(entity =>
        {
            entity.ToTable("Countries");
        });

        modelBuilder.Entity<Relay>(entity =>
        {
            entity.ToTable("Relays");
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
                new Category { Id = 2, Key = "results-masters",    Name = "Masters",      DisplayOrder = 2 },
                new Category { Id = 3, Key = "results-youth-team", Name = "Youth Team",   DisplayOrder = 3 }
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
                new AppRole { Id = 2, Name = "User" }
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
    }
}
