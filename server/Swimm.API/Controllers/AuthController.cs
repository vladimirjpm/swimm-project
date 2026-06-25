using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Swimm.API.Security;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;
using Swimm.Domain.Entities;

namespace Swimm.API.Controllers;

[Route("auth")]
public class AuthController : Controller
{
    private const string GoogleProvider = "Google";

    private readonly SwimmDbContext _db;
    private readonly ILocalAuthService _localAuth;

    public AuthController(SwimmDbContext db, ILocalAuthService localAuth)
    {
        _db = db;
        _localAuth = localAuth;
    }

    /// <summary>
    /// Перенаправление входа через Google OAuth. После авторизации Google перенаправит на returnUrl.
    /// </summary>
    [HttpGet("login/google")]
    public IActionResult LoginGoogle([FromQuery] string? returnUrl = "/")
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(GoogleCallback), new { returnUrl })
        };
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Callback после Google OAuth. Находит/создаёт пользователя и формирует cookie сессии.
    /// </summary>
    [HttpGet("google-callback")]
    public async Task<IActionResult> GoogleCallback([FromQuery] string? returnUrl = "/")
    {
        // Промежуточные claims от Google лежат в транзитной схеме (не в основной куке).
        var result = await HttpContext.AuthenticateAsync(AuthSchemes.External);
        if (result?.Principal == null)
            return Redirect("/auth/login");

        var claims = result.Principal;
        var providerKey = claims.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var email = claims.FindFirstValue(ClaimTypes.Email) ?? "";
        var name = claims.FindFirstValue(ClaimTypes.Name) ?? email;
        var avatar = claims.FindFirstValue("urn:google:picture")
                     ?? claims.FindFirstValue("picture");
        // Google возвращает email_verified — пригодится для безопасной линковки аккаунтов
        var emailVerified = string.Equals(claims.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(providerKey))
            return BadRequest("Google did not return a stable user id.");

        if (string.IsNullOrEmpty(email))
            return BadRequest("Google did not return an email.");

        // ── Безопасное разрешение аккаунта ───────────────────────────────────────
        // Первичный ключ идентичности — пара (Provider, ProviderKey): её провайдер
        // подтверждает криптографически в OAuth, подделать нельзя. Email НЕ является
        // первичным ключом поиска — иначе провайдер, не верифицирующий email, позволил бы
        // захват чужого аккаунта (account-takeover).
        var login = await _db.UserExternalLogins
            .Include(l => l.User).ThenInclude(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(l => l.Provider == GoogleProvider && l.ProviderKey == providerKey);

        AppUser user;
        if (login != null)
        {
            // Знакомая привязка — доверяем уже установленной связи, входим как её владелец.
            user = login.User;
            user.DisplayName = name;
            user.AvatarUrl = avatar ?? user.AvatarUrl;
            user.UpdatedAt = DateTime.UtcNow;

            login.Email = email;
            login.EmailVerified = emailVerified;
            login.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // Новая для этого провайдера идентичность. Связать с существующим email-аккаунтом
            // можно ТОЛЬКО если провайдер подтвердил владение email (email_verified).
            if (!emailVerified)
            {
                await HttpContext.SignOutAsync(AuthSchemes.External);
                return BadRequest(
                    "Google did not verify ownership of this email, so we can't sign you in or " +
                    "link the account automatically. Please verify your email with the provider.");
            }

            var emailUser = await _db.AppUsers
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Email == email);

            if (emailUser != null)
            {
                // Безопасная линковка: email подтверждён И аккаунт с таким email уже существует.
                user = emailUser;
                user.DisplayName = name;
                user.AvatarUrl = avatar ?? user.AvatarUrl;
                user.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                // Совершенно новый пользователь.
                user = new AppUser
                {
                    Email = email,
                    DisplayName = name,
                    AvatarUrl = avatar,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                _db.AppUsers.Add(user);
                await _db.SaveChangesAsync();

                // Роль User по умолчанию
                _db.AppUserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = 2 });
                await _db.SaveChangesAsync();

                await _db.Entry(user).Collection(u => u.UserRoles).Query()
                    .Include(ur => ur.Role).LoadAsync();
            }

            // Создаём привязку к разрешённому пользователю.
            login = new UserExternalLogin
            {
                UserId = user.Id,
                Provider = GoogleProvider,
                ProviderKey = providerKey,
                Email = email,
                EmailVerified = emailVerified,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.UserExternalLogins.Add(login);
        }

        // Деактивированный аккаунт не пускаем уже на входе (а не только при ре-валидации куки).
        if (!user.IsActive)
        {
            await HttpContext.SignOutAsync(AuthSchemes.External);
            return StatusCode(StatusCodes.Status403Forbidden, "This account is deactivated.");
        }

        // Сохраняем историю входа
        _db.UserLoginHistory.Add(new UserLoginHistory
        {
            UserId = user.Id,
            Provider = GoogleProvider,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            LoginAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        await IssueAuthCookieAsync(user);

        // Чистим транзитную куку OAuth-рукопожатия — она больше не нужна.
        await HttpContext.SignOutAsync(AuthSchemes.External);

        return Redirect(returnUrl ?? "/");
    }

    /// <summary>
    /// Выпускает основную cookie-сессию с ролями и SecurityStamp. Общий путь для Google и локального входа.
    /// </summary>
    private async Task IssueAuthCookieAsync(AppUser user)
    {
        var appClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
            // Штамп безопасности — сверяется при каждой ре-валидации куки (OnValidatePrincipal).
            // Несовпадение с БД → принудительный sign-out (отзыв доступа / смена ролей / сброс пароля).
            new(CookieSecurityStampValidator.SecurityStampClaim, user.SecurityStamp)
        };
        foreach (var ur in user.UserRoles)
            appClaims.Add(new Claim(ClaimTypes.Role, ur.Role.Name));

        var identity = new ClaimsIdentity(appClaims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    /// <summary>
    /// Текущий пользователь (без редиректа).
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Ok(new { isAuthenticated = false });

        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(email))
            return Ok(new { isAuthenticated = false });

        var user = await _db.AppUsers
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
            return Ok(new { isAuthenticated = false });

        return Ok(new
        {
            isAuthenticated = true,
            id = user.Id,
            email = user.Email,
            displayName = user.DisplayName,
            avatarUrl = user.AvatarUrl,
            roles = user.UserRoles.Select(r => r.Role.Name).ToArray(),
            swimmerId = user.SwimmerId
        });
    }

    /// <summary>
    /// Выход (только текущая сессия/устройство).
    /// </summary>
    [Authorize]
    [HttpGet("logout")]
    public async Task<IActionResult> Logout([FromQuery] string? returnUrl = "/")
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect(returnUrl ?? "/");
    }

    /// <summary>
    /// Выход со всех устройств. Бампает SecurityStamp пользователя → все остальные куки
    /// станут невалидными при следующей ре-валидации (OnValidatePrincipal). Текущая сессия
    /// завершается немедленно.
    /// </summary>
    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var user = await _db.AppUsers.FindAsync(userId);
        if (user != null)
        {
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "Signed out from all devices" });
    }

    // ── Локальный вход (email + пароль) ──────────────────────────────────────
    // Чувствительные к перебору эндпоинты ограничены rate-limiter'ом "auth".

    /// <summary>Регистрация. Anti-enumeration: при существующем email тоже возвращает 200.</summary>
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        var result = await _localAuth.RegisterAsync(req.Email, req.Password, req.DisplayName, BaseUrl());
        return result.Status switch
        {
            RegisterStatus.Ok => Ok(new { message = "If the address is valid, a confirmation email has been sent." }),
            RegisterStatus.WeakPassword => BadRequest(new { error = "Password must be at least 8 characters." }),
            RegisterStatus.InvalidEmail => BadRequest(new { error = "Invalid email address." }),
            _ => BadRequest()
        };
    }

    /// <summary>Локальный вход. На успех — выпускает cookie-сессию.</summary>
    [EnableRateLimiting("auth")]
    [HttpPost("login/local")]
    public async Task<IActionResult> LoginLocal([FromBody] LoginRequest req)
    {
        var result = await _localAuth.LoginAsync(req.Email, req.Password);
        switch (result.Status)
        {
            case LoginStatus.Ok:
                var user = await _db.AppUsers
                    .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Id == result.UserId);
                if (user == null) return Unauthorized(new { error = "Invalid email or password." });
                await IssueAuthCookieAsync(user);
                return Ok(new { message = "Signed in" });

            case LoginStatus.LockedOut:
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { error = "Too many failed attempts. Please try again later." });

            case LoginStatus.EmailNotConfirmed:
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "Please confirm your email before signing in." });

            default:
                return Unauthorized(new { error = "Invalid email or password." });
        }
    }

    /// <summary>Подтверждение email по ссылке из письма (GET — клик из почты).</summary>
    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string token)
    {
        var ok = await _localAuth.ConfirmEmailAsync(token);
        return HtmlPage(ok ? "✅ Email confirmed" : "❌ Invalid or expired link",
            ok ? "Your email is confirmed. You can now sign in." : "This confirmation link is invalid or has expired.");
    }

    /// <summary>Запрос сброса пароля. Всегда 200 (anti-enumeration).</summary>
    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        await _localAuth.ForgotPasswordAsync(req.Email, BaseUrl());
        return Ok(new { message = "If an account exists for this email, a reset link has been sent." });
    }

    /// <summary>Форма сброса пароля (GET — клик из письма). POST'ит на этот же путь.</summary>
    [HttpGet("reset-password")]
    public IActionResult ResetPasswordForm([FromQuery] string token)
    {
        var safeToken = System.Net.WebUtility.HtmlEncode(token ?? "");
        var body = $@"<form method=""post"" action=""/auth/reset-password"" style=""max-width:340px"">
            <input type=""hidden"" name=""token"" value=""{safeToken}"" />
            <p>Enter a new password (min 8 characters):</p>
            <input type=""password"" name=""newPassword"" minlength=""8"" required
                   style=""width:100%;padding:.5rem;margin-bottom:1rem"" />
            <button type=""submit"" style=""padding:.5rem 1rem"">Reset password</button>
        </form>";
        return HtmlPage("Reset your password", body, rawHtml: true);
    }

    /// <summary>Применение сброса пароля из формы. Инвалидирует все сессии (bump SecurityStamp).</summary>
    [EnableRateLimiting("auth")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromForm] string token, [FromForm] string newPassword)
    {
        var result = await _localAuth.ResetPasswordAsync(token, newPassword);
        return result.Status switch
        {
            ResetStatus.Ok => HtmlPage("✅ Password updated",
                "Your password has been changed and all existing sessions were signed out. You can now sign in."),
            ResetStatus.WeakPassword => HtmlPage("❌ Weak password",
                "Password must be at least 8 characters. Go back and try again."),
            _ => HtmlPage("❌ Invalid or expired link",
                "This reset link is invalid or has expired. Request a new one.")
        };
    }

    /// <summary>
    /// Страница входа — редирект для неавторизованных (вход выполняется через /auth/login/google).
    /// </summary>
    [HttpGet("login")]
    public IActionResult Login()
    {
        return Redirect("/");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private string BaseUrl() => $"{Request.Scheme}://{Request.Host}";

    private ContentResult HtmlPage(string title, string body, bool rawHtml = false)
    {
        var inner = rawHtml ? body : $"<p>{System.Net.WebUtility.HtmlEncode(body)}</p>";
        var html = $@"<!DOCTYPE html><html><head><meta charset=""utf-8""><title>{System.Net.WebUtility.HtmlEncode(title)}</title>
            <style>body{{font-family:sans-serif;max-width:480px;margin:4rem auto;padding:0 1rem;color:#222}}
            a{{color:#0b74de}}</style></head><body>
            <h2>{System.Net.WebUtility.HtmlEncode(title)}</h2>{inner}
            <p><a href=""/"">← Home</a></p></body></html>";
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = 200 };
    }

    public record RegisterRequest(string Email, string Password, string? DisplayName);
    public record LoginRequest(string Email, string Password);
    public record ForgotPasswordRequest(string Email);
}
