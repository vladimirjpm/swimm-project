using Microsoft.EntityFrameworkCore;
using Swimm.API.Models;

namespace Swimm.API.Data;

public class SwimmDbContext : DbContext
{
    public SwimmDbContext(DbContextOptions<SwimmDbContext> options) : base(options)
    {
    }

    /* === Справочники === */
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Swimmer> Swimmers => Set<Swimmer>();
    public DbSet<Relay> Relays => Set<Relay>();
    public DbSet<Gallery> Galleries => Set<Gallery>();
    public DbSet<GalleryItem> GalleryItems => Set<GalleryItem>();
    public DbSet<Style> Styles => Set<Style>();

    /* === Результаты === */
    public DbSet<ResultRecord> Results => Set<ResultRecord>();

    /* === Пользователи и доступ === */
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AppRole> AppRoles => Set<AppRole>();
    public DbSet<AppUserRole> AppUserRoles => Set<AppUserRole>();
    public DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();
    public DbSet<UserLoginHistory> UserLoginHistory => Set<UserLoginHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // --- Справочники ---

        modelBuilder.Entity<Competition>(entity =>
        {
            entity.ToTable("Competitions");
            entity.HasIndex(e => new { e.Name, e.Date, e.PoolType }).IsUnique();
        });

        modelBuilder.Entity<Club>(entity =>
        {
            entity.ToTable("Clubs");
        });

        modelBuilder.Entity<Swimmer>(entity =>
        {
            entity.ToTable("Swimmers");
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

        // --- Таблица результатов ---

        modelBuilder.Entity<ResultRecord>(entity =>
        {
            entity.ToTable("Results");

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

            entity.HasCheckConstraint("CK_Results_Heat_NonNegative", "[Heat] >= 0");
            entity.HasCheckConstraint("CK_Results_Lane_NonNegative", "[Lane] >= 0");
            entity.HasCheckConstraint("CK_Results_InternationalPoints_NonNegative", "[InternationalPoints] >= 0");
            entity.HasCheckConstraint("CK_Results_Position_PositiveOrNull", "[Position] IS NULL OR [Position] > 0");
            entity.HasCheckConstraint("CK_Results_PositionAgeGroup_PositiveOrNull", "[PositionAgeGroup] IS NULL OR [PositionAgeGroup] > 0");
        });

        // --- Пользователи и доступ ---

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("AppUsers");
            entity.HasIndex(e => e.Email).IsUnique();

            entity.HasOne(e => e.Swimmer)
                .WithMany()
                .HasForeignKey(e => e.SwimmerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Club)
                .WithMany()
                .HasForeignKey(e => e.ClubId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AppRole>(entity =>
        {
            entity.ToTable("AppRoles");
            entity.HasIndex(e => e.Name).IsUnique();

            entity.HasData(
                new AppRole { Id = 1, Name = "Admin" },
                new AppRole { Id = 2, Name = "User" }
            );
        });

        modelBuilder.Entity<AppUserRole>(entity =>
        {
            entity.ToTable("AppUserRoles");
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
            entity.ToTable("UserExternalLogins");
            entity.HasIndex(e => new { e.Provider, e.ProviderKey }).IsUnique();

            entity.HasOne(e => e.User)
                .WithMany(u => u.ExternalLogins)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserLoginHistory>(entity =>
        {
            entity.ToTable("UserLoginHistory");
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.LoginAt);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>
    /// Идемпотентный SQL-скрипт для нормализации схемы и денормализованных данных.
    /// Выполняется при каждом старте для поддержания целостности структуры.
    /// </summary>
    public const string NormalizeResultsSql = """
        -- 1. Relay: создать таблицу если её нет
        IF OBJECT_ID('dbo.Relays', 'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[Relays]
            (
                [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [TeamName] NVARCHAR(200) NULL,
                [SwimmersName] NVARCHAR(500) NULL
            );
        END;

        -- 2. Добавить RelayId если его нет
        IF COL_LENGTH('dbo.Results', 'RelayId') IS NULL
            ALTER TABLE [dbo].[Results] ADD [RelayId] INT NULL;

        -- 3. Добавить TimeMillisecond если его нет
        IF COL_LENGTH('dbo.Results', 'TimeMillisecond') IS NULL
            ALTER TABLE [dbo].[Results] ADD [TimeMillisecond] INT NULL;

        -- 4. Backfill TimeMillisecond из TimeOriginal
        IF COL_LENGTH('dbo.Results', 'TimeOriginal') IS NOT NULL
        BEGIN
            UPDATE r
            SET TimeMillisecond =
                CASE
                    WHEN r.[TimeOriginal] IS NULL OR LTRIM(RTRIM(r.[TimeOriginal])) = '' THEN NULL
                    WHEN PATINDEX('%[^0-9:.,]%', r.[TimeOriginal]) > 0 THEN NULL
                    WHEN CHARINDEX(':', r.[TimeOriginal]) > 0 THEN
                        TRY_CAST(
                            TRY_CAST(LEFT(r.[TimeOriginal], CHARINDEX(':', r.[TimeOriginal]) - 1) AS INT) * 60000
                            + TRY_CONVERT(DECIMAL(10,3), REPLACE(SUBSTRING(r.[TimeOriginal], CHARINDEX(':', r.[TimeOriginal]) + 1, 32), ',', '.')) * 1000
                        AS INT)
                    ELSE TRY_CAST(TRY_CONVERT(DECIMAL(10,3), REPLACE(r.[TimeOriginal], ',', '.')) * 1000 AS INT)
                END
            FROM dbo.Results r
            WHERE r.TimeMillisecond IS NULL AND r.[TimeOriginal] IS NOT NULL AND r.[TimeOriginal] <> '';
        END;

        -- 5. FK Relay (идемпотентно)
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Results_Relays_RelayId')
        BEGIN
            ALTER TABLE dbo.Results WITH CHECK
            ADD CONSTRAINT FK_Results_Relays_RelayId
                FOREIGN KEY (RelayId) REFERENCES dbo.Relays(Id) ON DELETE SET NULL;
        END;
    """;
}
