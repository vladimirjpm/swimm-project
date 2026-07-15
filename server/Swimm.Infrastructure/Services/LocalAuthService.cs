using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Локальный вход (email + пароль) поверх Sys_UserLocalCredentials.
/// Работает под swimm_rw-ролью (пишет в Sys_* таблицы). Cookie выпускает контроллер.
/// </summary>
public class LocalAuthService : ILocalAuthService
{
    private const int MinPasswordLength = 8;
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan EmailTokenTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResetTokenTtl = TimeSpan.FromHours(1);
    private const int DefaultUserRoleId = 2;

    // Фиктивный, но СТРУКТУРНО ВАЛИДНЫЙ argon2id-хеш для выравнивания тайминга при несуществующем
    // пользователе (mitigate user-enumeration по времени ответа). Соль/хеш — нули: Verify всё равно
    // прогонит полное вычисление Argon2 и вернёт false (пароль не совпадёт), сравняв время с реальной проверкой.
    private const string DummyHash =
        "argon2id$v=1$m=19456,t=2,p=1$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private readonly SwimmDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IEmailSender _email;

    public LocalAuthService(SwimmDbContext db, IPasswordHasher hasher, IEmailSender email)
    {
        _db = db;
        _hasher = hasher;
        _email = email;
    }

    public async Task<RegisterResult> RegisterAsync(string email, string password, string? displayName, string baseUrl, CancellationToken ct = default)
    {
        email = Normalize(email);
        if (!IsValidEmail(email))
            return RegisterResult.Fail(RegisterStatus.InvalidEmail);
        if (!IsStrongPassword(password))
            return RegisterResult.Fail(RegisterStatus.WeakPassword);

        var user = await _db.AppUsers
            .Include(u => u.LocalCredential)
            .Include(u => u.ExternalLogins)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user?.LocalCredential?.PasswordHash != null)
        {
            // Локальный аккаунт уже есть. Не раскрываем это (anti-enumeration): шлём письмо-напоминание,
            // отвечаем как при успехе. Сам аккаунт не трогаем.
            await _email.SendAsync(email, "Swimm — account already exists",
                "You already have an account with this email. Try logging in or resetting your password.", ct);
            return RegisterResult.Success();
        }

        if (user == null)
        {
            user = new AppUser
            {
                Email = email,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? email : displayName!,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.AppUsers.Add(user);
            await _db.SaveChangesAsync(ct);
            _db.AppUserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = DefaultUserRoleId });
        }

        // Если email уже подтверждён через верифицированный внешний вход — не требуем повторного подтверждения.
        var alreadyVerified = user.ExternalLogins.Any(l => l.EmailVerified &&
            string.Equals(l.Email, email, StringComparison.OrdinalIgnoreCase));

        var cred = user.LocalCredential ?? new UserLocalCredential { UserId = user.Id };
        cred.PasswordHash = _hasher.Hash(password);
        cred.PasswordAlgorithm = _hasher.Algorithm;
        cred.EmailConfirmed = alreadyVerified;
        cred.EmailConfirmedAt = alreadyVerified ? DateTime.UtcNow : null;
        cred.FailedLoginCount = 0;
        cred.LockoutEnd = null;
        cred.UpdatedAt = DateTime.UtcNow;
        if (user.LocalCredential == null)
            _db.UserLocalCredentials.Add(cred);

        await _db.SaveChangesAsync(ct);

        if (!alreadyVerified)
        {
            var token = await IssueTokenAsync(user.Id, SecurityTokenPurpose.EmailVerification, EmailTokenTtl, ct);
            var link = $"{baseUrl}/auth/verify-email?token={token}";
            await _email.SendAsync(email, "Swimm — confirm your email",
                $"Welcome! Confirm your email to activate local login:<br><a href=\"{link}\">{link}</a><br>Link expires in 24 hours.", ct);
        }

        return RegisterResult.Success();
    }

    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        email = Normalize(email);

        var user = await _db.AppUsers
            .Include(u => u.LocalCredential)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        var cred = user?.LocalCredential;
        if (user == null || cred?.PasswordHash == null)
        {
            // Выравниваем тайминг, чтобы по времени нельзя было отличить «нет юзера» от «неверный пароль».
            _hasher.Verify(password, DummyHash);
            return LoginResult.Fail(LoginStatus.InvalidCredentials);
        }

        if (cred.LockoutEnd is { } until && until > DateTime.UtcNow)
        {
            // Попытка входа во время lockout — тоже событие аудита (подбор пароля).
            _db.UserLoginHistory.Add(new UserLoginHistory
            {
                UserId = user.Id, Provider = "Local", Success = false, LoginAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
            return LoginResult.Locked(until);
        }

        if (!user.IsActive)
            return LoginResult.Fail(LoginStatus.InvalidCredentials);

        if (!_hasher.Verify(password, cred.PasswordHash))
        {
            cred.FailedLoginCount++;
            LoginStatus status = LoginStatus.InvalidCredentials;
            if (cred.FailedLoginCount >= MaxFailedLogins)
            {
                cred.LockoutEnd = DateTime.UtcNow.Add(LockoutDuration);
                cred.FailedLoginCount = 0;
                status = LoginStatus.LockedOut;
            }
            cred.UpdatedAt = DateTime.UtcNow;
            _db.UserLoginHistory.Add(new UserLoginHistory
            {
                UserId = user.Id, Provider = "Local", Success = false, LoginAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
            return status == LoginStatus.LockedOut
                ? LoginResult.Locked(cred.LockoutEnd!.Value)
                : LoginResult.Fail(LoginStatus.InvalidCredentials);
        }

        if (!cred.EmailConfirmed)
            return LoginResult.Fail(LoginStatus.EmailNotConfirmed);

        // Успех: сбрасываем счётчики, пишем историю.
        cred.FailedLoginCount = 0;
        cred.LockoutEnd = null;
        cred.UpdatedAt = DateTime.UtcNow;
        _db.UserLoginHistory.Add(new UserLoginHistory
        {
            UserId = user.Id,
            Provider = "Local",
            LoginAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        return LoginResult.Success(user.Id);
    }

    public async Task<bool> ConfirmEmailAsync(string token, CancellationToken ct = default)
    {
        var entry = await FindValidTokenAsync(token, SecurityTokenPurpose.EmailVerification, ct);
        if (entry == null) return false;

        entry.ConsumedAt = DateTime.UtcNow;

        var cred = await _db.UserLocalCredentials.FirstOrDefaultAsync(c => c.UserId == entry.UserId, ct);
        if (cred != null)
        {
            cred.EmailConfirmed = true;
            cred.EmailConfirmedAt = DateTime.UtcNow;
            cred.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ForgotPasswordAsync(string email, string baseUrl, CancellationToken ct = default)
    {
        email = Normalize(email);
        var user = await _db.AppUsers
            .Include(u => u.LocalCredential)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        // Письмо шлём только если есть локальный аккаунт; контроллер в любом случае отвечает одинаково.
        if (user?.LocalCredential?.PasswordHash == null)
            return;

        var token = await IssueTokenAsync(user.Id, SecurityTokenPurpose.PasswordReset, ResetTokenTtl, ct);
        var link = $"{baseUrl}/auth/reset-password?token={token}";
        await _email.SendAsync(email, "Swimm — reset your password",
            $"Reset your password:<br><a href=\"{link}\">{link}</a><br>Link expires in 1 hour. If you didn't request this, ignore this email.", ct);
    }

    public async Task<ResetResult> ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default)
    {
        if (!IsStrongPassword(newPassword))
            return ResetResult.Fail(ResetStatus.WeakPassword);

        var entry = await FindValidTokenAsync(token, SecurityTokenPurpose.PasswordReset, ct);
        if (entry == null)
            return ResetResult.Fail(ResetStatus.InvalidOrExpiredToken);

        var user = await _db.AppUsers
            .Include(u => u.LocalCredential)
            .FirstOrDefaultAsync(u => u.Id == entry.UserId, ct);
        if (user?.LocalCredential == null)
            return ResetResult.Fail(ResetStatus.InvalidOrExpiredToken);

        entry.ConsumedAt = DateTime.UtcNow;

        // Гасим все прочие невыданные reset-токены этого пользователя.
        await _db.UserSecurityTokens
            .Where(t => t.UserId == user.Id && t.Purpose == SecurityTokenPurpose.PasswordReset
                        && t.ConsumedAt == null && t.Id != entry.Id)
            .ForEachAsync(t => t.ConsumedAt = DateTime.UtcNow, ct);

        var cred = user.LocalCredential;
        cred.PasswordHash = _hasher.Hash(newPassword);
        cred.PasswordAlgorithm = _hasher.Algorithm;
        cred.FailedLoginCount = 0;
        cred.LockoutEnd = null;
        // Успешный сброс по ссылке из письма доказывает владение email.
        cred.EmailConfirmed = true;
        cred.EmailConfirmedAt ??= DateTime.UtcNow;
        cred.UpdatedAt = DateTime.UtcNow;

        // Инвалидация всех активных сессий (см. OnValidatePrincipal).
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return ResetResult.Success(user.Id);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<string> IssueTokenAsync(int userId, SecurityTokenPurpose purpose, TimeSpan ttl, CancellationToken ct)
    {
        // Гасим прежние невыданные токены того же назначения.
        await _db.UserSecurityTokens
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.ConsumedAt == null)
            .ForEachAsync(t => t.ConsumedAt = DateTime.UtcNow, ct);

        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        _db.UserSecurityTokens.Add(new UserSecurityToken
        {
            UserId = userId,
            Purpose = purpose,
            TokenHash = Sha256Hex(raw),
            ExpiresAt = DateTime.UtcNow.Add(ttl),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        return raw;
    }

    private async Task<UserSecurityToken?> FindValidTokenAsync(string raw, SecurityTokenPurpose purpose, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var hash = Sha256Hex(raw);
        return await _db.UserSecurityTokens.FirstOrDefaultAsync(t =>
            t.TokenHash == hash && t.Purpose == purpose &&
            t.ConsumedAt == null && t.ExpiresAt > DateTime.UtcNow, ct);
    }

    private static string Normalize(string email) => (email ?? "").Trim().ToLowerInvariant();

    private static bool IsStrongPassword(string password) =>
        !string.IsNullOrWhiteSpace(password) && password.Length >= MinPasswordLength;

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        return MailAddress.TryCreate(email, out var addr) && addr!.Address == email;
    }

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
