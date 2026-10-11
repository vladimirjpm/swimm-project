using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Data.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> entity)
    {
        entity.ToTable("Sys_AppUsers");
        entity.HasIndex(e => e.Email).IsUnique();

        entity.HasOne(e => e.Swimmer)
            .WithMany()
            .HasForeignKey(e => e.SwimmerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class AppRoleConfiguration : IEntityTypeConfiguration<AppRole>
{
    public void Configure(EntityTypeBuilder<AppRole> entity)
    {
        entity.ToTable("Sys_AppRoles");
        entity.HasIndex(e => e.Name).IsUnique();

        entity.HasData(
            new AppRole { Id = 1, Name = "Admin" },
            new AppRole { Id = 2, Name = "User" },
            new AppRole { Id = 3, Name = "Coach" }
        );
    }
}

internal sealed class AppUserRoleConfiguration : IEntityTypeConfiguration<AppUserRole>
{
    public void Configure(EntityTypeBuilder<AppUserRole> entity)
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
    }
}

internal sealed class UserExternalLoginConfiguration : IEntityTypeConfiguration<UserExternalLogin>
{
    public void Configure(EntityTypeBuilder<UserExternalLogin> entity)
    {
        entity.ToTable("Sys_UserExternalLogins");
        entity.HasIndex(e => new { e.Provider, e.ProviderKey }).IsUnique();

        entity.HasOne(e => e.User)
            .WithMany(u => u.ExternalLogins)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

    }
}

// Локальные учётные данные (email+пароль) — связь 1:1 с AppUser по общему PK.
// Таблица заведена заранее; логика локального входа пока не реализована.
internal sealed class UserLocalCredentialConfiguration : IEntityTypeConfiguration<UserLocalCredential>
{
    public void Configure(EntityTypeBuilder<UserLocalCredential> entity)
    {
        entity.ToTable("Sys_UserLocalCredentials");
        entity.HasKey(e => e.UserId);

        entity.HasOne(e => e.User)
            .WithOne(u => u.LocalCredential)
            .HasForeignKey<UserLocalCredential>(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserLoginHistoryConfiguration : IEntityTypeConfiguration<UserLoginHistory>
{
    public void Configure(EntityTypeBuilder<UserLoginHistory> entity)
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
    }
}

// Одноразовые токены (email-verify / password-reset). Храним только хеш токена.
internal sealed class UserSecurityTokenConfiguration : IEntityTypeConfiguration<UserSecurityToken>
{
    public void Configure(EntityTypeBuilder<UserSecurityToken> entity)
    {
        entity.ToTable("Sys_UserSecurityTokens");
        entity.HasIndex(e => e.TokenHash);
        entity.HasIndex(e => new { e.UserId, e.Purpose });

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
