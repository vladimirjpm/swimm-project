using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Аудит логинов для панели Admin/Users: запись неудачных попыток в Sys_UserLoginHistory,
/// принудительный выход (бамп SecurityStamp) и сводные счётчики LoginStats.
/// ExecuteDelete/ExecuteUpdate (ретеншн, LastSeenAt) InMemory не поддерживает — не покрываем.
/// </summary>
public class AdminLoginAuditTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private const string Email = "user@example.com";
    private const string Hash = "fake-hash";

    private static async Task<AppUser> SeedUserAsync(SwimmDbContext db, bool confirmed = true)
    {
        var user = new AppUser { Email = Email, DisplayName = "U", SecurityStamp = Guid.NewGuid().ToString("N") };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        db.UserLocalCredentials.Add(new UserLocalCredential
        {
            UserId = user.Id, PasswordHash = Hash, PasswordAlgorithm = "argon2id-v1", EmailConfirmed = confirmed
        });
        await db.SaveChangesAsync();
        return user;
    }

    private static LocalAuthService BuildAuth(SwimmDbContext db, bool passwordOk)
    {
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Algorithm).Returns("argon2id-v1");
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns(Hash);
        hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(passwordOk);
        return new LocalAuthService(db, hasher.Object, Mock.Of<IEmailSender>());
    }

    private static AdminRepository BuildRepo(SwimmDbContext db) =>
        new(db, Mock.Of<ICacheService>());

    // ── запись событий ────────────────────────────────────────────────────────

    [Fact]
    public async Task FailedLogin_WritesFailureEvent()
    {
        await using var db = CreateDb(nameof(FailedLogin_WritesFailureEvent));
        var user = await SeedUserAsync(db);

        await BuildAuth(db, passwordOk: false).LoginAsync(Email, "wrong");

        var ev = Assert.Single(db.UserLoginHistory.Where(h => h.UserId == user.Id));
        Assert.False(ev.Success);
        Assert.Equal("Local", ev.Provider);
    }

    [Fact]
    public async Task SuccessfulLogin_WritesSuccessEvent()
    {
        await using var db = CreateDb(nameof(SuccessfulLogin_WritesSuccessEvent));
        var user = await SeedUserAsync(db);

        var result = await BuildAuth(db, passwordOk: true).LoginAsync(Email, "right");

        Assert.Equal(LoginStatus.Ok, result.Status);
        var ev = Assert.Single(db.UserLoginHistory.Where(h => h.UserId == user.Id));
        Assert.True(ev.Success);
    }

    [Fact]
    public async Task LoginDuringLockout_WritesFailureEvent()
    {
        await using var db = CreateDb(nameof(LoginDuringLockout_WritesFailureEvent));
        var user = await SeedUserAsync(db);
        var cred = await db.UserLocalCredentials.SingleAsync(c => c.UserId == user.Id);
        cred.LockoutEnd = DateTime.UtcNow.AddMinutes(10);
        await db.SaveChangesAsync();

        var result = await BuildAuth(db, passwordOk: true).LoginAsync(Email, "any");

        Assert.Equal(LoginStatus.LockedOut, result.Status);
        var ev = Assert.Single(db.UserLoginHistory.Where(h => h.UserId == user.Id));
        Assert.False(ev.Success);
    }

    // ── принудительный выход ─────────────────────────────────────────────────

    [Fact]
    public async Task ForceSignOut_BumpsSecurityStamp_KeepsActive()
    {
        await using var db = CreateDb(nameof(ForceSignOut_BumpsSecurityStamp_KeepsActive));
        var user = await SeedUserAsync(db);
        var stampBefore = user.SecurityStamp;

        var ok = await BuildRepo(db).ForceSignOutAsync(user.Id);

        Assert.True(ok);
        var fresh = await db.AppUsers.SingleAsync(u => u.Id == user.Id);
        Assert.NotEqual(stampBefore, fresh.SecurityStamp);
        Assert.True(fresh.IsActive);
    }

    [Fact]
    public async Task ForceSignOut_UnknownUser_ReturnsFalse()
    {
        await using var db = CreateDb(nameof(ForceSignOut_UnknownUser_ReturnsFalse));
        Assert.False(await BuildRepo(db).ForceSignOutAsync(12345));
    }

    // ── счётчики ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginStats_CountsOnlineWindowsAndPeriods()
    {
        await using var db = CreateDb(nameof(LoginStats_CountsOnlineWindowsAndPeriods));
        var now = DateTime.UtcNow;

        var online = new AppUser { Email = "a@x.com", DisplayName = "A", LastSeenAt = now.AddMinutes(-5) };
        var offline = new AppUser { Email = "b@x.com", DisplayName = "B", LastSeenAt = now.AddHours(-2) };
        var disabledButRecent = new AppUser { Email = "c@x.com", DisplayName = "C", IsActive = false, LastSeenAt = now.AddMinutes(-1) };
        db.AppUsers.AddRange(online, offline, disabledButRecent);
        await db.SaveChangesAsync();

        db.UserLoginHistory.AddRange(
            new UserLoginHistory { UserId = online.Id, Provider = "Local", Success = true, LoginAt = now.AddDays(-1) },
            new UserLoginHistory { UserId = online.Id, Provider = "Google", Success = true, LoginAt = now.AddDays(-20) },
            new UserLoginHistory { UserId = offline.Id, Provider = "Local", Success = false, LoginAt = now.AddDays(-2) },
            new UserLoginHistory { UserId = offline.Id, Provider = "Local", Success = false, LoginAt = now.AddDays(-40) });
        await db.SaveChangesAsync();

        var stats = await BuildRepo(db).GetLoginStatsAsync();

        Assert.Equal(1, stats.OnlineNow);       // только активный и свежий
        Assert.Equal(1, stats.Logins7d);        // успешный за сутки
        Assert.Equal(2, stats.Logins30d);       // + успешный 20 дней назад
        Assert.Equal(1, stats.FailedLogins7d);  // фейл 40 дней назад не считается
    }

    [Fact]
    public async Task GetUsers_IncludesPerUserLoginCounts()
    {
        await using var db = CreateDb(nameof(GetUsers_IncludesPerUserLoginCounts));
        var user = await SeedUserAsync(db);
        var now = DateTime.UtcNow;
        db.UserLoginHistory.AddRange(
            new UserLoginHistory { UserId = user.Id, Provider = "Local", Success = true, LoginAt = now.AddDays(-1) },
            new UserLoginHistory { UserId = user.Id, Provider = "Local", Success = true, LoginAt = now.AddDays(-10) },
            new UserLoginHistory { UserId = user.Id, Provider = "Local", Success = false, LoginAt = now.AddDays(-1) });
        await db.SaveChangesAsync();

        var row = Assert.Single(await BuildRepo(db).GetUsersAsync(), u => u.Id == user.Id);

        Assert.Equal(1, row.Logins7d);   // фейл не считается
        Assert.Equal(2, row.Logins30d);
        Assert.True(row.HasLocalPassword);
        Assert.False(row.HasGoogle);
    }
}
