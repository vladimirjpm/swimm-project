using Microsoft.EntityFrameworkCore;
using Swimm.API.Models;

namespace Swimm.API.Data;

public class SwimmDbContext : DbContext
{
    public SwimmDbContext(DbContextOptions<SwimmDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AppRole> Roles => Set<AppRole>();
    public DbSet<AppUserRole> UserRoles => Set<AppUserRole>();
    public DbSet<UserExternalLogin> ExternalLogins => Set<UserExternalLogin>();
    public DbSet<UserLoginHistory> LoginHistory => Set<UserLoginHistory>();
    public DbSet<Swimmer> Swimmers => Set<Swimmer>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<ResultRecord> Results => Set<ResultRecord>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Style> Styles => Set<Style>();
    public DbSet<Relay> Relays => Set<Relay>();
    public DbSet<Gallery> Galleries => Set<Gallery>();
    public DbSet<GalleryItem> GalleryItems => Set<GalleryItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Связь многие-ко-многим через AppUserRole
        modelBuilder.Entity<AppUserRole>()
            .HasKey(ur => new { ur.UserId, ur.RoleId });

        modelBuilder.Entity<AppUserRole>()
            .HasOne(ur => ur.User)
            .WithMany(u => u.UserRoles)
            .HasForeignKey(ur => ur.UserId);

        modelBuilder.Entity<AppUserRole>()
            .HasOne(ur => ur.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(ur => ur.RoleId);

        // Уникальный email
        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // Уникальный provider+key
        modelBuilder.Entity<UserExternalLogin>()
            .HasIndex(e => new { e.Provider, e.ProviderKey })
            .IsUnique();

        // Сид начальной роли Admin
        modelBuilder.Entity<AppRole>().HasData(
            new AppRole { Id = 1, Name = "Admin" },
            new AppRole { Id = 2, Name = "User" }
        );
    }
}
