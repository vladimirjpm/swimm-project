using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

public class LocalAuthServiceTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static DbContextOptions<SwimmDbContext> BuildOptions(string name) =>
        new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static SwimmDbContext CreateDb(string name) =>
        new SwimmDbContext(BuildOptions(name));

    private const string ValidEmail    = "user@example.com";
    private const string ValidPassword = "StrongPass1!";
    private const string FakeHash      = "fake-hash";
    private const string FakeAlgorithm = "argon2id-v1";

    /// <summary>Создаёт согласованную пару моков; verifyReturns управляет IPasswordHasher.Verify.</summary>
    private static (Mock<IPasswordHasher> hasher, Mock<IEmailSender> email) Mocks(
        bool verifyReturns = true)
    {
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Algorithm).Returns(FakeAlgorithm);
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns(FakeHash);
        hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(verifyReturns);

        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return (hasher, email);
    }

    private static LocalAuthService BuildSvc(SwimmDbContext db,
        Mock<IPasswordHasher> hasher, Mock<IEmailSender> email) =>
        new(db, hasher.Object, email.Object);

    /// <summary>Сеет пользователя с подтверждённым локальным аккаунтом.</summary>
    private static async Task<AppUser> SeedConfirmedUserAsync(SwimmDbContext db)
    {
        var user = new AppUser
        {
            Email = ValidEmail, DisplayName = "Test User",
            IsActive = true, SecurityStamp = Guid.NewGuid().ToString("N")
        };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        db.UserLocalCredentials.Add(new UserLocalCredential
        {
            UserId           = user.Id,
            PasswordHash     = FakeHash,
            PasswordAlgorithm = FakeAlgorithm,
            EmailConfirmed   = true
        });
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>SHA-256 hex — минимальный дубль для прямого посева токенов в БД.</summary>
    private static string Sha256Hex(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ── Register ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_InvalidEmail_ReturnsInvalidEmail()
    {
        await using var db = CreateDb(nameof(Register_InvalidEmail_ReturnsInvalidEmail));
        var (hasher, email) = Mocks();

        var result = await BuildSvc(db, hasher, email)
            .RegisterAsync("not-an-email", ValidPassword, null, "https://app.com");

        Assert.Equal(RegisterStatus.InvalidEmail, result.Status);
    }

    [Fact]
    public async Task Register_WeakPassword_ReturnsWeakPassword()
    {
        await using var db = CreateDb(nameof(Register_WeakPassword_ReturnsWeakPassword));
        var (hasher, email) = Mocks();

        var result = await BuildSvc(db, hasher, email)
            .RegisterAsync(ValidEmail, "short", null, "https://app.com");

        Assert.Equal(RegisterStatus.WeakPassword, result.Status);
    }

    [Fact]
    public async Task Register_NewUser_ReturnsOkAndSendsVerificationEmail()
    {
        await using var db = CreateDb(nameof(Register_NewUser_ReturnsOkAndSendsVerificationEmail));
        var (hasher, emailMock) = Mocks();

        var result = await BuildSvc(db, hasher, emailMock)
            .RegisterAsync(ValidEmail, ValidPassword, "Alice", "https://app.com");

        Assert.Equal(RegisterStatus.Ok, result.Status);
        emailMock.Verify(e => e.SendAsync(
            ValidEmail,
            It.Is<string>(s => s.Contains("confirm")),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_DuplicateLocalAccount_ReturnsOkAndSendsReminderEmail()
    {
        await using var db = CreateDb(nameof(Register_DuplicateLocalAccount_ReturnsOkAndSendsReminderEmail));
        var (hasher, emailMock) = Mocks();
        var svc = BuildSvc(db, hasher, emailMock);

        await svc.RegisterAsync(ValidEmail, ValidPassword, null, "https://app.com");
        var result = await svc.RegisterAsync(ValidEmail, ValidPassword, null, "https://app.com");

        Assert.Equal(RegisterStatus.Ok, result.Status);
        emailMock.Verify(e => e.SendAsync(
            ValidEmail,
            It.Is<string>(s => s.Contains("already exists")),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Login ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_UserNotFound_ReturnsFail()
    {
        await using var db = CreateDb(nameof(Login_UserNotFound_ReturnsFail));
        var (hasher, email) = Mocks(verifyReturns: false);

        var result = await BuildSvc(db, hasher, email)
            .LoginAsync("nobody@example.com", "any");

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
    }

    [Fact]
    public async Task Login_WrongPassword_IncrementsFailedLoginCount()
    {
        await using var db = CreateDb(nameof(Login_WrongPassword_IncrementsFailedLoginCount));
        var (hasher, email) = Mocks(verifyReturns: false);
        var user = await SeedConfirmedUserAsync(db);

        await BuildSvc(db, hasher, email).LoginAsync(ValidEmail, "wrong");

        var cred = await db.UserLocalCredentials.FindAsync(user.Id);
        Assert.Equal(1, cred!.FailedLoginCount);
    }

    [Fact]
    public async Task Login_MaxFailedAttempts_SetsLockoutAndReturnsLockedOut()
    {
        await using var db = CreateDb(nameof(Login_MaxFailedAttempts_SetsLockoutAndReturnsLockedOut));
        var (hasher, email) = Mocks(verifyReturns: false);
        await SeedConfirmedUserAsync(db);
        var svc = BuildSvc(db, hasher, email);

        LoginResult last = null!;
        for (var i = 0; i < 5; i++)
            last = await svc.LoginAsync(ValidEmail, "wrong");

        Assert.Equal(LoginStatus.LockedOut, last.Status);
        Assert.NotNull(last.LockoutEnd);
    }

    [Fact]
    public async Task Login_EmailNotConfirmed_ReturnsFail()
    {
        await using var db = CreateDb(nameof(Login_EmailNotConfirmed_ReturnsFail));
        var (hasher, email) = Mocks(verifyReturns: true);
        var user = new AppUser
        {
            Email = ValidEmail, DisplayName = "Test",
            IsActive = true, SecurityStamp = Guid.NewGuid().ToString("N")
        };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        db.UserLocalCredentials.Add(new UserLocalCredential
        {
            UserId = user.Id, PasswordHash = FakeHash,
            PasswordAlgorithm = FakeAlgorithm, EmailConfirmed = false
        });
        await db.SaveChangesAsync();

        var result = await BuildSvc(db, hasher, email).LoginAsync(ValidEmail, ValidPassword);

        Assert.Equal(LoginStatus.EmailNotConfirmed, result.Status);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsSuccessWithUserId()
    {
        await using var db = CreateDb(nameof(Login_ValidCredentials_ReturnsSuccessWithUserId));
        var (hasher, email) = Mocks(verifyReturns: true);
        var user = await SeedConfirmedUserAsync(db);

        var result = await BuildSvc(db, hasher, email).LoginAsync(ValidEmail, ValidPassword);

        Assert.Equal(LoginStatus.Ok, result.Status);
        Assert.Equal(user.Id, result.UserId);
    }

    // ── ConfirmEmail ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmEmail_ValidToken_SetsEmailConfirmedAndReturnsTrue()
    {
        await using var db = CreateDb(nameof(ConfirmEmail_ValidToken_SetsEmailConfirmedAndReturnsTrue));
        var (hasher, email) = Mocks();
        var user = new AppUser
        {
            Email = ValidEmail, DisplayName = "Test",
            IsActive = true, SecurityStamp = Guid.NewGuid().ToString("N")
        };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        db.UserLocalCredentials.Add(new UserLocalCredential
        {
            UserId = user.Id, PasswordHash = FakeHash,
            PasswordAlgorithm = FakeAlgorithm, EmailConfirmed = false
        });

        const string rawToken = "confirm-raw-token-test-value";
        db.UserSecurityTokens.Add(new UserSecurityToken
        {
            UserId = user.Id,
            Purpose = SecurityTokenPurpose.EmailVerification,
            TokenHash = Sha256Hex(rawToken),
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var ok = await BuildSvc(db, hasher, email).ConfirmEmailAsync(rawToken);

        Assert.True(ok);
        var cred = await db.UserLocalCredentials.FindAsync(user.Id);
        Assert.True(cred!.EmailConfirmed);
    }

    // ── ResetPassword ────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_WeakPassword_ReturnsFail()
    {
        await using var db = CreateDb(nameof(ResetPassword_WeakPassword_ReturnsFail));
        var (hasher, email) = Mocks();

        var result = await BuildSvc(db, hasher, email).ResetPasswordAsync("any-token", "short");

        Assert.Equal(ResetStatus.WeakPassword, result.Status);
    }

    [Fact]
    public async Task ResetPassword_InvalidToken_ReturnsFail()
    {
        await using var db = CreateDb(nameof(ResetPassword_InvalidToken_ReturnsFail));
        var (hasher, email) = Mocks();

        var result = await BuildSvc(db, hasher, email).ResetPasswordAsync("bad-token", ValidPassword);

        Assert.Equal(ResetStatus.InvalidOrExpiredToken, result.Status);
    }

    [Fact]
    public async Task ResetPassword_ValidToken_ReturnsOkAndUpdatesPasswordHash()
    {
        await using var db = CreateDb(nameof(ResetPassword_ValidToken_ReturnsOkAndUpdatesPasswordHash));
        var (hasher, email) = Mocks();
        var user = await SeedConfirmedUserAsync(db);

        const string rawToken = "reset-raw-token-test-value";
        db.UserSecurityTokens.Add(new UserSecurityToken
        {
            UserId = user.Id,
            Purpose = SecurityTokenPurpose.PasswordReset,
            TokenHash = Sha256Hex(rawToken),
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        hasher.Setup(h => h.Hash(ValidPassword)).Returns("new-fake-hash");

        var result = await BuildSvc(db, hasher, email).ResetPasswordAsync(rawToken, ValidPassword);

        Assert.Equal(ResetStatus.Ok, result.Status);
        var cred = await db.UserLocalCredentials.FindAsync(user.Id);
        Assert.Equal("new-fake-hash", cred!.PasswordHash);
    }
}
