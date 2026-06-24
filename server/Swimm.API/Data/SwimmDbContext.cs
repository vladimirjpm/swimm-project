using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;

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
    public DbSet<Country> Countries => Set<Country>();

    /* === Результаты === */
    public DbSet<ResultRecord> Results => Set<ResultRecord>();

    /* === Импорт === */
    public DbSet<ImportHistory> ImportHistory => Set<ImportHistory>();

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
    }
}
